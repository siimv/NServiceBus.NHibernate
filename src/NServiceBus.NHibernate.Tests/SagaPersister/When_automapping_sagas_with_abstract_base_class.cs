namespace NServiceBus.SagaPersisters.NHibernate.Tests
{
    using System;
    using Features;
    using global::NHibernate.Cfg;
    using global::NHibernate.Impl;
    using NUnit.Framework;
    using Saga;
    using Settings;

    [TestFixture]
    public class When_autoMapping_sagas_with_abstract_base_class
    {
        private SessionFactoryImpl sessionFactory;

        [SetUp]
        public void SetUp()
        {
            var builder = new NHibernateSagaStorage();
            var properties = SQLiteConfiguration.InMemory();

            var configuration = new Configuration().AddProperties(properties);
            var settings = new SettingsHolder();
            settings.Set("TypesToScan", new[] { typeof(SagaWithAbstractBaseClass), typeof(ContainSagaData), typeof(MyOwnAbstractBase) });
            builder.ApplyMappings(settings, configuration);
            sessionFactory = configuration.BuildSessionFactory() as SessionFactoryImpl;
        }

        [Test]
        public void Should_not_generate_join_table_for_base_class()
        {
            var persister = sessionFactory.GetEntityPersister(typeof(SagaWithAbstractBaseClass).FullName).
                   ClassMetadata as global::NHibernate.Persister.Entity.UnionSubclassEntityPersister;

            Assert.IsNotNull(persister);
        }       
    }
    
    public class SagaWithAbstractBaseClass : MyOwnAbstractBase
    {
        public virtual Guid OrderId { get; set; }
    }

    public abstract class MyOwnAbstractBase : IContainSagaData
    {
        public virtual Guid Id { get; set; }
        public virtual string Originator { get; set; }
        public virtual string OriginalMessageId { get; set; }
    }
   
}