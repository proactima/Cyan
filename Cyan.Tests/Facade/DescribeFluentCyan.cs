﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Cyan.Fluent;
using Cyan.Tests.Helpers;
using FakeItEasy;
using FluentAssertions;
using NUnit.Framework;

namespace Cyan.Tests.Facade
{
    [TestFixture]
    public class DescribeFluentCyan
    {

        [Test]
        public void ItShouldReturnNotFound_WhenQueryingForOneRecord_GivenNoRecordsExists()
        {
            // g
            var expected = new Response<TemporaryObject>(HttpStatusCode.NotFound, null);
            var client = new FluentCyan<TemporaryObject>(FluentCyanHelper.GetCyanClient());

            // w
            var actual = client.FromTable("dummy")
                               .Retrieve("123");

            // t
            actual.ShouldBeEquivalentTo(expected);
        }


        [Test]
        public void ItShouldDefineTheTableName()
        {
            // g
            const string tableName = "dummy";
            var fakeClient = FluentCyanHelper.GetFakeCyanClient();
            var client = new FluentCyan<TemporaryObject>(fakeClient);

            // w
            client.FromTable(tableName);

            // t
            A.CallTo(() => fakeClient.TryCreateTable(tableName)).MustHaveHappened();
        }

        [Test]
        public void ItComplainsWhenPassingInEmptyTableName()
        {
            // g
            const string tableName = "";
            var fakeClient = FluentCyanHelper.GetFakeCyanClient();
            var client = new FluentCyan<TemporaryObject>(fakeClient);

            // w
            Action act = () => client.FromTable(tableName);

            // t
            act.ShouldThrow<ArgumentNullException>();
        }

        [Test]
        public void ItComplainsWhenPassingInInvalidTableName()
        {
            // g
            const string tableName = "123";
            var client = new FluentCyan<TemporaryObject>(FluentCyanHelper.GetCyanClient());

            // w
            Action act = () => client.FromTable(tableName);

            // t
            act.ShouldThrow<ArgumentException>();
        }

        [Test]
        public void ItShouldReturnOK_WhenQueringForOneRecord_GivenRecordExists()
        {
            // g
            var objectId = Guid.NewGuid().ToString();
            var expected = new Response<TemporaryObject>(HttpStatusCode.OK, new TemporaryObject("PK", objectId) { Id = objectId });
            var table = FluentCyanHelper.GetAzureTable<TemporaryObject>();
            table.Add(expected.Result);


            var client = new FluentCyan<object>(FluentCyanHelper.GetCyanClient());

            // w
            dynamic actual = client.FromTable("TemporaryObject")
                                   .Retrieve(objectId);

            // t
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.Result.Id, Is.EqualTo(expected.Result.Id));
            Assert.That(actual.Result.PartitionKey, Is.EqualTo(expected.Result.PartitionKey));
            Assert.That(actual.Result.RowKey, Is.EqualTo(expected.Result.RowKey));
            Assert.That(actual.Result.ETag.Replace(":", "%3A"), Is.EqualTo(expected.Result.ETag));
            Assert.That((DateTimeOffset)(actual.Result.Timestamp), Is.EqualTo(expected.Result.Timestamp));
        }

        [Test]
        public void ItShouldReturnNotFound_WhenRetrievingAllRecords_GivenNoRecordsExists()
        {
            // g
            var expected = new Response<TemporaryObject>(HttpStatusCode.NotFound, null);
            var client = new FluentCyan<TemporaryObject>(FluentCyanHelper.GetCyanClient());

            // w
            var actual = client.FromTable("dummy")
                               .RetrieveAll();

            // t
            actual.ShouldBeEquivalentTo(expected);
        }

        [Test]
        public void ItShouldReturnOK_WhenQueringForAllRecords_GivenRecordsExists()
        {
            // g
            var item1 = new TemporaryObject("PK", Guid.NewGuid().ToString()) { Id = "item1" };
            var item2 = new TemporaryObject("PK", Guid.NewGuid().ToString()) { Id = "item2" };
            var table = FluentCyanHelper.GetAzureTable<TemporaryObject>();
            table.Add(item1);
            table.Add(item2);

            var allObjects = new[] { item1, item2 };
            var expected = new Response<TemporaryObject[]>(HttpStatusCode.OK, allObjects);

            var client = new FluentCyan<TemporaryObject>(FluentCyanHelper.GetCyanClient());

            // w
            dynamic actual = client.FromTable("TemporaryObject").RetrieveAll();

            // t
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.Result.Length, Is.EqualTo(expected.Result.Length));
        }

        [Test]
        public void ItShouldReturnOK_WhenQueringWithFiltersForRecords_GivenRecordsExists()
        {
            // g
            var item1 = new TemporaryObject("PK", Guid.NewGuid().ToString()) { Id = "item1", SomeValue = "a" };
            var item2 = new TemporaryObject("PK", Guid.NewGuid().ToString()) { Id = "item2", SomeValue = "b" };
            var table = FluentCyanHelper.GetAzureTable<TemporaryObject>();
            table.Add(item1);
            table.Add(item2);

            var filteredObjects = new[] { item2 };
            var expected = new Response<TemporaryObject[]>(HttpStatusCode.OK, filteredObjects);

            var client = new FluentCyan<TemporaryObject>(FluentCyanHelper.GetCyanClient());

            // w
            dynamic actual = client.FromTable("TemporaryObject")
                                   .RetrieveBy(item => item.SomeValue == "b");

            // t
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.Result.Length, Is.EqualTo(expected.Result.Length));
        }

        [SetUp]
        public void Setup()
        {
        }

        [TearDown]
        public void Teardown()
        {
            var table = FluentCyanHelper.GetAzureTable<TemporaryObject>();
            var tobeDeleted = table.GetAll();
            foreach (var tableObject in tobeDeleted)
            {
                table.Delete(tableObject);
            }
        }


    }
}