﻿namespace NServiceBus.Unicast.Subscriptions.NHibernate.Tests
{
    using System.Threading.Tasks;
    using NServiceBus.Extensibility;
    using NUnit.Framework;

    [TestFixture]
    class When_receiving_a_subscription_message : InMemoryDBFixture
    {
        [Test]
        public async Task A_subscription_entry_should_be_added_to_the_database()
        {
            var messageTypes = new[] { new MessageType(typeof(MessageA)), new MessageType(typeof(MessageB)) };

            await storage.Subscribe(TestClients.ClientA, messageTypes, new ContextBag());

            using (var session = SessionFactory.OpenSession())
            {
                var subscriptions = session.CreateCriteria(typeof(Subscription)).List<Subscription>();
                Assert.AreEqual(subscriptions.Count, 2);
            }
        }

        [Test]
        public async Task Duplicate_subscription_shouldnt_create_additional_db_rows()
        {
            await storage.Subscribe(TestClients.ClientA, MessageTypes.MessageA, new ContextBag());
            await storage.Subscribe(TestClients.ClientA, MessageTypes.MessageA, new ContextBag());

            using (var session = SessionFactory.OpenSession())
            {
                var subscriptions = session.CreateCriteria(typeof(Subscription)).List<Subscription>();
                Assert.AreEqual(subscriptions.Count, 1);
            }
        }
    }
}
