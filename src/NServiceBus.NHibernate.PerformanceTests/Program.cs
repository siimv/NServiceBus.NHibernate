﻿namespace Runner
{
    using System;
    using System.Configuration;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Transactions;
    using NServiceBus;
    using NServiceBus.Features;
    using NServiceBus.Persistence.NHibernate;
    using Saga;

    internal class Program
    {
        static void Main(string[] args)
        {
            var numberOfThreads = int.Parse(args[0]);
            var volatileMode = (args[4].ToLower() == "volatile");
            var suppressDTC = (args[4].ToLower() == "suppressdtc");
            var twoPhaseCommit = (args[4].ToLower() == "twophasecommit");
            var outbox = (args[4].ToLower() == "outbox");
            var saga = (args[5].ToLower() == "sagamessages");
            var publish = (args[5].ToLower() == "publishmessages");
            var concurrency = int.Parse(args[7]);

            TransportConfigOverride.MaximumConcurrencyLevel = numberOfThreads;

            var numberOfMessages = int.Parse(args[1]);

            var endpointName = "PerformanceTest";

            if (volatileMode)
            {
                endpointName += ".Volatile";
            }

            if (suppressDTC)
            {
                endpointName += ".SuppressDTC";
            }

            if (outbox)
            {
                endpointName += ".outbox";
            }

            var config = new BusConfiguration();
            config.EndpointName(endpointName);
            config.UseTransport<MsmqTransport>().Transactions(suppressDTC ? TransportTransactionMode.SendsAtomicWithReceive : TransportTransactionMode.TransactionScope);
            config.EnableInstallers();

            switch (args[2].ToLower())
            {
                case "xml":
                    config.UseSerialization<XmlSerializer>();
                    break;

                case "json":
                    config.UseSerialization<JsonSerializer>();
                    break;

                default:
                    throw new InvalidOperationException("Illegal serialization format " + args[2]);
            }

            config.DisableFeature<Audit>();

            NHibernateSettingRetriever.ConnectionStrings = () => new ConnectionStringSettingsCollection
                {
                    new ConnectionStringSettings("NServiceBus/Persistence", SqlServerConnectionString)
                };

            config.UsePersistence<NHibernatePersistence>();
            if (outbox)
            {
                config.EnableOutbox();
            }

            config.RunWhenEndpointStartsAndStops(new Loader(async session =>
            {
                if (saga)
                {
                    await SeedSagaMessages(session, numberOfMessages, endpointName, concurrency).ConfigureAwait(false);
                }
                else if (publish)
                {
                    Statistics.PublishTimeNoTx = await PublishEvents(session, numberOfMessages / 2, numberOfThreads, false).ConfigureAwait(false);
                    Statistics.PublishTimeWithTx = await PublishEvents(session, numberOfMessages / 2, numberOfThreads, !outbox).ConfigureAwait(false);
                }
                else
                {
                    Statistics.SendTimeNoTx = await SeedInputQueue(session, numberOfMessages / 2, endpointName, numberOfThreads, false, twoPhaseCommit).ConfigureAwait(false);
                    Statistics.SendTimeWithTx = await SeedInputQueue(session, numberOfMessages / 2, endpointName, numberOfThreads, !outbox, twoPhaseCommit).ConfigureAwait(false);
                }
                }));

                PerformTest(args, config, saga, numberOfMessages, endpointName, concurrency, publish, numberOfThreads, outbox, twoPhaseCommit).GetAwaiter().GetResult();
        }

        class Loader : IWantToRunWhenBusStartsAndStops
        {
            Func<IBusSession, Task> loadAction;

            public Loader(Func<IBusSession, Task> loadAction)
            {
                this.loadAction = loadAction;
            }

            public Task Start(IBusSession session)
            {
                return loadAction(session);
            }

            public Task Stop(IBusSession session)
            {
                return Task.FromResult(0);
            }
        }

        static async Task PerformTest(string[] args, BusConfiguration config, bool saga, int numberOfMessages, string endpointName, int concurrency, bool publish, int numberOfThreads, bool outbox, bool twoPhaseCommit)
        {
            var startableBus = await Endpoint.Create(config).ConfigureAwait(false);


            Statistics.StartTime = DateTime.Now;

            await startableBus.Start().ConfigureAwait(false);

            while (Interlocked.Read(ref Statistics.NumberOfMessages) < numberOfMessages)
            {
                Thread.Sleep(1000);
            }

            DumpSetting(args);
            Statistics.Dump();
        }


        static void DumpSetting(string[] args)
        {
            Console.Out.WriteLine("---------------- Settings ----------------");
            Console.Out.WriteLine("Threads: {0}, Serialization: {1}, Transport: {2}, Messagemode: {3}",
                args[0],
                args[2],
                args[3],
                args[5]);
        }

        static async Task SeedSagaMessages(IBusSession bus, int numberOfMessages, string inputQueue, int concurrency)
        {
            for (var i = 0; i < numberOfMessages / concurrency; i++)
            {
                for (var j = 0; j < concurrency; j++)
                {
                    await bus.Send(inputQueue, new StartSagaMessage
                    {
                        Id = i
                    }).ConfigureAwait(false);
                }
            }
        }

        static async Task<TimeSpan> SeedInputQueue(IBusSession bus, int numberOfMessages, string inputQueue, int numberOfThreads, bool createTransaction, bool twoPhaseCommit)
        {
            var sw = new Stopwatch();
            sw.Start();
            var tasks = Enumerable.Range(0, numberOfThreads)
                .Select(i => Task.Factory.StartNew( async () =>
            {
                for (var j = 0; j < numberOfMessages/numberOfThreads; i++)
                {
                    var message = CreateMessage();
                    message.TwoPhaseCommit = twoPhaseCommit;
                    message.Id = j;

                    if (createTransaction)
                    {
                        using (var tx = new TransactionScope())
                        {
                            await bus.Send(inputQueue, message).ConfigureAwait(false);
                            tx.Complete();
                        }
                    }
                    else
                    {
                        await bus.Send(inputQueue, message).ConfigureAwait(false);
                    }
                }   
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            sw.Stop();
            return sw.Elapsed;
        }

        static async Task<TimeSpan> PublishEvents(IBusSession bus, int numberOfMessages, int numberOfThreads, bool createTransaction)
        {
            var sw = new Stopwatch();
            sw.Start();
            var tasks = Enumerable.Range(0, numberOfThreads)
                .Select(i => Task.Factory.StartNew(async () =>
                {
                    for (var j = 0; j < numberOfMessages/numberOfThreads; i++)
                    {
                        if (createTransaction)
                        {
                            using (var tx = new TransactionScope())
                            {
                                await bus.Publish<TestEvent>().ConfigureAwait(false);
                                tx.Complete();
                            }
                        }
                        else
                        {
                            await bus.Publish<TestEvent>().ConfigureAwait(false);
                        }
                        Interlocked.Increment(ref Statistics.NumberOfMessages);
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);
            sw.Stop();
            return sw.Elapsed;
        }

        static MessageBase CreateMessage()
        {
            return new TestMessage();
        }

        static string SqlServerConnectionString = @"Server=localhost\sqlexpress;Database=nservicebus;Trusted_Connection=True;";
    }
}