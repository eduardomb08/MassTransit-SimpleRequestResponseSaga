using Automatonymous;
using Automatonymous.Events;
using MassTransit;
using MassTransit.Saga;
using System;
using System.Threading.Tasks;

namespace SimpleRequestResponseSaga
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            var bus = Bus.Factory.CreateUsingInMemory(cfg =>
            {
                cfg.ReceiveEndpoint("req_resp_saga", ec =>
                {
                    ec.Consumer<MyRequestConsumer>();
                    //ec.Consumer<MyResponseConsumer>();

                    var saga = new MySaga();
                    var repo = new InMemorySagaRepository<MySagaState>();
                    ec.StateMachineSaga(saga, repo);
                });
            });

            bus.Start();

            try
            {
                ExecuteSaga(bus);

                await Console.Out.WriteLineAsync("Press any key to exit...");
                Console.ReadKey();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            finally
            {
                try
                {
                    bus.Stop();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private static void ExecuteSaga(IBusControl bus)
        {
            var address = new Uri($"loopback://localhost/req_resp_saga");

            var requestClient = new MessageRequestClient<IStartSaga, MyResponse>(bus, address, TimeSpan.FromSeconds(30));
            var response = requestClient.Request(new { CorrelationId = Guid.NewGuid(), Data = "Please do this" })
                .GetAwaiter()
                .GetResult();

            Console.WriteLine($"This was the result: {response.ResponseMessage}");
        }
    }

    public class MySagaState : SagaStateMachineInstance
    {
        public Guid? RequestId { get; set; }

        public Uri ResponseAddress { get; set; }

        public Guid CorrelationId { get; set; }

        public State CurrentState { get; set; }

        public string Data { get; set; }
    }

    public interface IStartSaga
    {
        Guid CorrelationId { get; }
        string Data { get; }
    }

    public interface IResponseReady
    {
        Guid CorrelationId { get; }
        string Data { get; }
        Guid? RequestId { get; }
    }

    public class MySaga : MassTransitStateMachine<MySagaState>
    {
        public static Uri address = new Uri($"loopback://localhost/req_resp_saga");

        public Event<IStartSaga> StartSaga { get; private set; }

        public Event<IResponseReady> ResponseReady { get; private set; }

        public Request<MySagaState, MyRequest, MyResponse> SomeRequest { get; private set; }

        public MySaga()
        {
            InstanceState(s => s.CurrentState);

            Event(() => StartSaga,
                cc =>
                    cc.CorrelateBy(state => state.Data, context => context.Message.Data)
                        .SelectId(context => Guid.NewGuid()));

            Event(() => ResponseReady, cc => cc.CorrelateById(context => context.Message.CorrelationId));

            Request(() => SomeRequest, x => x.RequestId, cfg =>
            {
                cfg.ServiceAddress = address;
                cfg.SchedulingServiceAddress = address;
                cfg.Timeout = TimeSpan.FromSeconds(30);
            });

            Initially(
                When(StartSaga)
                    .Then(context =>
                    {
                        context.Instance.Data = context.Data.Data;

                        SagaConsumeContext<MySagaState, IStartSaga> payload;
                        if (context.TryGetPayload(out payload))
                        {
                            context.Instance.ResponseAddress = payload.ResponseAddress;
                            context.Instance.RequestId = payload.RequestId;
                        }
                    })
                    .ThenAsync(context => Console.Out.WriteLineAsync($"Saga started: \"{context.Data.Data}\" received"))
                    .TransitionTo(SomeRequest.Pending)
                    .Send(address, context => new MyRequest() { CorrelationId = context.Instance.CorrelationId, RequestMessage = context.Data.Data })
            );

            During(SomeRequest.Pending,
                When(ResponseReady)
                    .ThenAsync(context => Console.Out.WriteLineAsync($"Saga replied: \"{context.Data.Data}\""))
                    .ThenAsync(async context =>
                    {
                        var endpoint = await context.GetSendEndpoint(context.Instance.ResponseAddress);
                        await endpoint.Send(new MyResponse() { CorrelationId = context.Instance.CorrelationId, ResponseMessage = context.Data.Data }, c => c.RequestId = context.Instance.RequestId );
                    })
            );

            During(SomeRequest.Pending,
                When(SomeRequest.Completed)
                    .ThenAsync(context => Console.Out.WriteLineAsync($"Saga ended"))
                    .Finalize());
        }
    }

    public class MyRequest
    {
        public Guid CorrelationId { get; set; }
        public string RequestMessage { get; set; }
    }

    public class MyResponse
    {
        public Guid CorrelationId { get; set; }
        public string ResponseMessage { get; set; }
    }

    public class MyRequestConsumer : IConsumer<MyRequest>
    {
        public async Task Consume(ConsumeContext<MyRequest> context)
        {
            await Console.Out.WriteLineAsync($"Request: {context.Message.RequestMessage}");

            // Some long running task
            await Task.Delay(3000);

            await context.RespondAsync<IResponseReady>(new
                { CorrelationId = context.Message.CorrelationId, Data = "Your wish is my command" });
        }
    }

    //public class MyResponseConsumer : IConsumer<MyResponse>
    //{
    //    public async Task Consume(ConsumeContext<MyResponse> context)
    //    {
    //        await Console.Out.WriteLineAsync($"Response: {context.Message.ResponseMessage}");
    //    }
    //}
}

