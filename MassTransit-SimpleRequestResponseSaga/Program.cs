using Automatonymous;
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
                    ec.Consumer<MyResponseConsumer>();

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
            var endPoint = bus.GetSendEndpoint(address)
                .Result;

            endPoint.Send<IStartSagaCommand>(new { Data = "Hello World!!" });

            //var requestClient = new MessageRequestClient<MyRequest, MyResponse>(bus, address, TimeSpan.FromSeconds(30));
            //var response = requestClient.Request(new MyRequest() { CorrelationId = Guid.NewGuid(), RequestMessage = "Please do this" })
            //    .GetAwaiter()
            //    .GetResult();

            //Console.WriteLine($"This was the result: {response.Message.ResponseMessage}");

            Console.WriteLine($"This should be the last line in the console");
        }
    }

    public class MySagaState : SagaStateMachineInstance
    {
        public Guid? NullableCorrelationId { get; set; }

        public Guid CorrelationId { get; set; }

        public State CurrentState { get; set; }

        public string Data { get; set; }
    }

    public interface IStartSagaCommand
    {
        string Data { get; }
    }

    public class MySaga : MassTransitStateMachine<MySagaState>
    {
        public static Uri address = new Uri($"loopback://localhost/req_resp_saga");

        public Event<IStartSagaCommand> StartSaga { get; private set; }
        
        public Request<MySagaState, MyRequest, MyResponse> SomeRequest { get; private set; }

        public MySaga()
        {
            InstanceState(s => s.CurrentState);

            Event(() => StartSaga,
                cc =>
                    cc.CorrelateBy(state => state.Data, context => context.Message.Data)
                        .SelectId(context => Guid.NewGuid()));

            Request(() => SomeRequest, x => x.NullableCorrelationId, cfg =>
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
                    })
                    .ThenAsync(
                        context => Console.Out.WriteLineAsync($"Saga started: " +
                                                              $" {context.Data.Data} received"))
                    .Request(SomeRequest, context => new MyRequest() { CorrelationId = context.Instance.CorrelationId, RequestMessage = "Please do this" })
                    .TransitionTo(SomeRequest.Pending)
                    .ThenAsync(context => Console.Out.WriteLineAsync($"Transition completed: " +
                                                                     $" {(context.Instance.CurrentState == SomeRequest.Pending ? "pending" : "done")} received"))
                    //.Then(context =>
                    //{
                    //    var endpoint = context.GetSendEndpoint(address).GetAwaiter().GetResult();
                    //    endpoint.Send(new MyResponse() { CorrelationId = context.Instance.CorrelationId, ResponseMessage = "Your wish is my command" });
                    //})
            );

            During(SomeRequest.Pending,
                When(SomeRequest.Completed)
                    .ThenAsync(
                        context => Console.Out.WriteLineAsync($"Saga ended: " +
                                                              $" {context.Data.ResponseMessage} received"))
                    .Finalize()
            );
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

            await context.RespondAsync(new MyResponse()
            { CorrelationId = context.Message.CorrelationId, ResponseMessage = "Your wish is my command" });
        }
    }

    public class MyResponseConsumer : IConsumer<MyResponse>
    {
        public async Task Consume(ConsumeContext<MyResponse> context)
        {
            await Console.Out.WriteLineAsync($"Response: {context.Message.ResponseMessage}");
        }
    }
}

