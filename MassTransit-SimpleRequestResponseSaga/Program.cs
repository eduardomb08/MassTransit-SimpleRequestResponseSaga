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

            var requestClient = new MessageRequestClient<MyRequest, MyResponse>(bus, address, TimeSpan.FromSeconds(30));
            var response = requestClient.Request(new MyRequest() { CorrelationId = Guid.NewGuid(), RequestMessage = "Please do this" })
                .GetAwaiter()
                .GetResult();

            Console.WriteLine($"This was the result: {response.ResponseMessage}");
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

        public State Pending { get; private set; }
        public State Done { get; private set; }

        public Event<MyRequest> SomeRequest { get; private set; }

        public MySaga()
        {
            InstanceState(s => s.CurrentState);

            Event(() => SomeRequest,
                cc =>
                    cc.CorrelateBy(state => state.Data, context => context.Message.RequestMessage)
                        .SelectId(context => Guid.NewGuid()));

            Initially(
                When(SomeRequest)
                    .Then(context =>
                    {
                        context.Instance.Data = context.Data.RequestMessage;
                    })
                    .ThenAsync(
                        context => Console.Out.WriteLineAsync($"Saga started: " +
                                                              $" {context.Data.RequestMessage} received"))
                    //.Request(SomeRequest, context => new MyRequest() { CorrelationId = context.Instance.CorrelationId, RequestMessage = "Please do this" })
                    .TransitionTo(Pending)
                    .Respond(context => new MyResponse() { CorrelationId = context.Instance.CorrelationId, ResponseMessage = "You got it!"})
                    .TransitionTo(Done)
                    .ThenAsync(context => Console.Out.WriteLineAsync($"Transition completed: " +
                                                                     $" {(context.Instance.CurrentState == Pending ? "pending" : "done")} received"))
                    //.Then(context =>
                    //{
                    //    var endpoint = context.GetSendEndpoint(address).GetAwaiter().GetResult();
                    //    endpoint.Send(new MyResponse() { CorrelationId = context.Instance.CorrelationId, ResponseMessage = "Your wish is my command" });
                    //})
            );

            //During(SomeRequest.Pending,
            //    When(SomeRequest.Completed)
            //        .ThenAsync(
            //            context => Console.Out.WriteLineAsync($"Saga ended: " +
            //                                                  $" {context.Data.ResponseMessage} received"))
            //        .Finalize()
            //);
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

            //await context.RespondAsync(new MyResponse()
            //{ CorrelationId = context.Message.CorrelationId, ResponseMessage = "Your wish is my command" });
        }
    }
}

