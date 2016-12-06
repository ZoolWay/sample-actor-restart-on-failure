using System;
using System.Diagnostics;
using Akka.Actor;

namespace actor_restart_on_failure
{
    public class Program
    {
        public class ConfigObject
        {
            public string ConfigText { get; set; }
            public int ConfigValue { get; set; }
        }

        public class ProcessAndFailMessage
        {
        }

        public class ChildWorkerActor : ReceiveActor
        {
            private readonly IActorRef parent;
            private readonly ConfigObject config;

            public ChildWorkerActor(IActorRef parent, ConfigObject config)
            {
                Console.WriteLine(String.Format("{0}: Constructing", Self.Path.ToStringWithoutAddress()));
                this.parent = parent;
                this.config = config;
                Receive<ProcessAndFailMessage>((message) => ReceivedProcessAndFail(message));
            }

            protected override void PreStart()
            {
                Console.WriteLine(String.Format("{0}: PreStart", Self.Path.ToStringWithoutAddress()));
            }

            protected override void PostStop()
            {
                Console.WriteLine(String.Format("{0}: PostStop", Self.Path.ToStringWithoutAddress()));
            }

            protected override void PreRestart(Exception reason, object message)
            {
                Console.WriteLine(String.Format("{0}: PreRestart because of '{1}'", Self.Path.ToStringWithoutAddress(), reason.GetType().FullName));
                base.PreRestart(reason, message);
            }

            protected override void PostRestart(Exception reason)
            {
                Console.WriteLine(String.Format("{0}: PostRestart because of '{1}'", Self.Path.ToStringWithoutAddress(), reason.GetType().FullName));
                base.PostRestart(reason);
            }

            private bool ReceivedProcessAndFail(ProcessAndFailMessage message)
            {
                Console.WriteLine(String.Format("{0}: Processing", Self.Path.ToStringWithoutAddress()));
                Debug.WriteLine("Doing something and then...");
                throw new Exception("Oh no, something failed. Lets start over...");
                Debug.WriteLine("Never reached!");
            }
        }

        public class ParentActor : ReceiveActor
        {
            private readonly ConfigObject config;
            private IActorRef childWorker;

            public ParentActor(ConfigObject config)
            {
                Console.WriteLine(String.Format("{0}: Constructing", Self.Path.ToStringWithoutAddress()));
                this.childWorker = ActorRefs.Nobody;
                this.config = config;
            }

            protected override void PreStart()
            {
                Console.WriteLine(String.Format("{0}: PreStart", Self.Path.ToStringWithoutAddress()));
                Props childProps = Props.Create(() => new ChildWorkerActor(Self, this.config));
                this.childWorker = Context.ActorOf(childProps, "child-worker");
                Console.WriteLine(String.Format("{0}: Created child '{1}', now scheduling the message", Self.Path.ToStringWithoutAddress(), this.childWorker.Path.ToStringWithoutAddress()));
                Context.System.Scheduler.ScheduleTellRepeatedly(7000, 5000, this.childWorker, new ProcessAndFailMessage(), Self);
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(
                    maxNrOfRetries: 3,
                    withinTimeRange: TimeSpan.FromSeconds(5),
                    decider: Decider.From(x =>
                    {
                        Directive d = Directive.Restart;
                        if (x is ActorInitializationException) d = Directive.Stop;
                        else if (x is ActorKilledException) d = Directive.Stop;
                        Console.WriteLine(String.Format("Providing supervisor strategy for exception '{0}' - decided on '{1}'", x.GetType().FullName, d));
                        return d;
                    }));
            }
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("Creating config");
            ConfigObject config = new ConfigObject() { ConfigText = "lore ipsum", ConfigValue = 42 };

            Console.WriteLine("Creating actorsystem");
            var system = ActorSystem.Create("TestSystem");

            Console.WriteLine("Creating parent actor");
            Props propsParent = Props.Create(() => new ParentActor(config));
            var parent = system.ActorOf(propsParent, "parent");

            Console.ReadLine();
        }
    }
}
