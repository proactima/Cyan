﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Web;
using Cyan.Interfaces;

namespace Cyan
{
    [DebuggerDisplay("CyanTable({TableName})")]
    public class CyanTable : ICyanTable
    {
        public ICyanRest RestClient { get; set; }

        public CyanTable(string tableName, CyanClient client)
        {
            TableName = tableName;
            RestClient = client.RestClient;
        }

        public string TableName { get; private set; }

        #region Operations on Entities

        /// <summary>
        /// Queries entities in a table.
        /// </summary>
        /// <param name="partition">The partition-key.</param>
        /// <param name="row">The row key.</param>
        /// <param name="filter">The query expression.</param>
        /// <param name="top">Maximum number of entities to be returned.</param>
        /// <param name="disableContinuation">If <code>true</code> disables automatic query continuation.</param>
        /// <param name="fields">Names of the properties to be returned.</param>
        /// <returns>Entities matching your query.</returns>
        public IEnumerable<CyanEntity> Query(string partition = null,
            string row = null,
            string filter = null,
            int top = 0,
            bool disableContinuation = false,
            params string[] fields)
        {
            var single = !string.IsNullOrEmpty(partition) && !string.IsNullOrEmpty(row);

            var resource = FormatResource(partition, row);

            var returned = 0;
            bool hasContinuation;
            string nextPartition = null;
            string nextRow = null;
            do
            {
                var query = FormatQuery(partition, row, filter, top, fields, nextPartition, nextRow);

                var response = RestClient.GetRequest(resource, query);

                if (single)
                {
                    // should not throw NotFound, should return empty
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        yield break;

                    response.ThrowIfFailed();
                    yield return CyanSerializer.DeserializeEntity(response.ResponseBody.Root);
                }

                response.ThrowIfFailed();

                // just one | because both statements must be executed everytime
                hasContinuation = response.Headers.TryGetValue("x-ms-continuation-NextPartitionKey", out nextPartition)
                                  | response.Headers.TryGetValue("x-ms-continuation-NextRowKey", out nextRow);

                var entities = CyanSerializer.DeserializeEntities(response.ResponseBody.Root);
                foreach (var entity in entities)
                {
                    yield return entity;
                    if (top > 0 && ++returned >= top)
                        break;
                }
            } while (!disableContinuation // continuation has not been disabled
                     && hasContinuation // the response has a valid continuation
                     && !(top > 0 && returned >= top));
            // if there is a top argument and we didn't return enough entities
        }

        /// <summary>
        /// Inserts a new entity into a table.
        /// </summary>
        /// <param name="entity">The entity to be inserted.</param>
        /// <returns>The entity that has been inserted.</returns>
        public CyanEntity Insert(CyanEntity entity)
        {
            CyanEntity ret;
            InsertImpl(entity, out ret, true);

            return ret;
        }

        public bool TryInsert(CyanEntity entity)
        {
            CyanEntity dummy;
            return TryInsert(entity, out dummy);
        }

        public bool TryInsert(CyanEntity entity, out CyanEntity insertedEntity)
        {
            return InsertImpl(entity, out insertedEntity, false);
        }

        private bool InsertImpl(CyanEntity entity, out CyanEntity insertedEntity, bool throwOnConflict)
        {
            var cyanEntity = CyanEntity.FromObject(entity);

            var document = cyanEntity.Serialize();

            var response = RestClient.PostRequest(TableName, document.ToString());

            if (response.StatusCode == HttpStatusCode.Conflict
                && !throwOnConflict)
            {
                insertedEntity = null;
                return false;
            }

            response.ThrowIfFailed();

            insertedEntity = CyanSerializer.DeserializeEntity(response.ResponseBody.Root);
            return true;
        }

        /// <summary>
        /// Updates an existing entity in a table replacing it.
        /// </summary>
        /// <param name="entity">The entity to be updated.</param>
        /// <param name="unconditionalUpdate">If set to <code>true</code> optimistic concurrency is off.</param>
        public void Update(CyanEntity entity, bool unconditionalUpdate = false)
        {
            UpdateImpl(entity, true, unconditionalUpdate);
        }

        /// <summary>
        /// Tries to update an existing entity in a table replacing it.
        /// </summary>
        /// <param name="entity">The entity to be updated.</param>
        /// <returns><code>true</code> if the entity ETag matches.</returns>
        public bool TryUpdate(CyanEntity entity)
        {
            return UpdateImpl(entity, false, false);
        }

        private bool UpdateImpl(CyanEntity entity, bool throwOnPreconditionFailure, bool unconditionalUpdate)
        {
            var cyanEntity = CyanEntity.FromObject(entity);

            var partition = cyanEntity.PartitionKey;
            var row = cyanEntity.RowKey;
            var eTag = cyanEntity.ETag;

            var document = cyanEntity.Serialize();

            var resource = FormatResource(partition, row);

            var response = RestClient.PutRequest(resource, document.ToString(), unconditionalUpdate ? "*" : eTag);

            // update entity etag for future updates
            string newETag;
            if (response.Headers.TryGetValue("ETag", out newETag))
                cyanEntity.ETag = HttpUtility.UrlDecode(newETag);

            if (!throwOnPreconditionFailure
                && !unconditionalUpdate
                && response.PreconditionFailed)
                return false;

            response.ThrowIfFailed();
            return true;
        }

        /// <summary>
        /// Updates an existing entity in a table by updating the entity's properties.
        /// </summary>
        /// <param name="entity">The entity to be updated.</param>
        /// <param name="unconditionalUpdate">If set to <code>true</code> optimistic concurrency is off.</param>
        /// <param name="fields">The name of the fields to be updated.</param>
        public void Merge(CyanEntity entity, bool unconditionalUpdate = false, params string[] fields)
        {
            MergeImpl(entity, true, unconditionalUpdate);
        }

        /// <summary>
        /// Tries to update an existing entity in a table by updating the entity's properties.
        /// </summary>
        /// <param name="entity">The entity to be updated.</param>
        /// <param name="fields">The name of the fields to be updated.</param>
        /// <returns><code>true</code> if the entity ETag matches.</returns>
        public bool TryMerge(CyanEntity entity, params string[] fields)
        {
            return MergeImpl(entity, false, false, fields);
        }

        private bool MergeImpl(CyanEntity entity, bool throwOnPreconditionFailure, bool unconditionalUpdate,
            params string[] fields)
        {
            var cyanEntity = CyanEntity.FromObject(entity);

            var partition = cyanEntity.PartitionKey;
            var row = cyanEntity.RowKey;
            var eTag = cyanEntity.ETag;

            var filteredEntity = (fields != null && fields.Length > 0)
                ? CyanEntity.FromEntity(cyanEntity, fields)
                : cyanEntity;

            var document = filteredEntity.Serialize();

            var resource = FormatResource(partition, row);

            var response = RestClient.MergeRequest(resource, document.ToString(), unconditionalUpdate ? "*" : eTag);

            // update entity etag for future updates
            string newETag;
            if (response.Headers.TryGetValue("ETag", out newETag))
                cyanEntity.ETag = HttpUtility.UrlDecode(newETag);

            if (!throwOnPreconditionFailure
                && !unconditionalUpdate
                && response.PreconditionFailed)
                return false;

            response.ThrowIfFailed();
            return true;
        }

        /// <summary>
        /// Deletes an existing entity from a table.
        /// </summary>
        /// <param name="entity">The entity to be deleted.</param>
        /// <param name="unconditionalUpdate">If set to <code>true</code> optimistic concurrency is off.</param>
        public void Delete(CyanEntity entity, bool unconditionalUpdate = false)
        {
            var cyanEntity = CyanEntity.FromObject(entity);

            Delete(cyanEntity.PartitionKey,
                cyanEntity.RowKey,
                unconditionalUpdate ? null : cyanEntity.ETag);
        }

        /// <summary>
        /// Deletes an existing entity from a table.
        /// </summary>
        /// <param name="partition">The partition-key of the entity to be deleted.</param>
        /// <param name="row">The row-key of the entity to be deleted.</param>
        /// <param name="eTag">The ETag to be passed as "If-Match" header. Omit or <code>null</code> for "*".</param>
        public void Delete(string partition, string row, string eTag = null)
        {
            var resource = FormatResource(partition, row);

            var response = RestClient.DeleteRequest(resource, eTag);
            response.ThrowIfFailed();
        }

        public CyanEntity InsertOrUpdate(CyanEntity entity)
        {
            var cyanEntity = CyanEntity.FromObject(entity);

            var partition = cyanEntity.PartitionKey;
            var row = cyanEntity.RowKey;

            var document = cyanEntity.Serialize();
            var resource = FormatResource(partition, row);

            var response = RestClient.PutRequest(resource, document.ToString());
            response.ThrowIfFailed();

            // update entity etag for future updates
            string newETag;
            if (response.Headers.TryGetValue("ETag", out newETag))
                cyanEntity.ETag = HttpUtility.UrlDecode(newETag);

            return cyanEntity;
        }

        public CyanEntity InsertOrMerge(CyanEntity entity, params string[] fields)
        {
            var cyanEntity = CyanEntity.FromObject(entity);

            var partition = cyanEntity.PartitionKey;
            var row = cyanEntity.RowKey;

            var filteredEntity = (fields != null && fields.Length > 0)
                ? CyanEntity.FromEntity(cyanEntity, fields)
                : cyanEntity;

            var document = filteredEntity.Serialize();
            var resource = FormatResource(partition, row);

            var response = RestClient.MergeRequest(resource, document.ToString());
            response.ThrowIfFailed();

            // update entity etag for future updates
            string newETag;
            if (response.Headers.TryGetValue("ETag", out newETag))
                cyanEntity.ETag = HttpUtility.UrlDecode(newETag);

            return cyanEntity;
        }

        public ICyanEGT Batch()
        {
            return new CyanEGT(this);
        }

        #endregion

        private static string FormatQuery(string partition, string row, string filter, int top, string[] fields,
            string nextPartition, string nextRow)
        {
            var hasPartition = !string.IsNullOrEmpty(partition);
            var hasRow = !string.IsNullOrEmpty(row);
            if (hasPartition ^ hasRow)
            {
                var indexer = hasPartition
                    ? string.Format("PartitionKey eq '{0}'", partition)
                    : string.Format("RowKey eq '{0}'", row);

                filter = string.IsNullOrEmpty(filter)
                    ? indexer
                    : string.Format("{0} and ({1})", indexer, filter);
            }

            if (string.IsNullOrEmpty(filter)
                && top <= 0
                && (fields == null || fields.Length == 0)
                && string.IsNullOrEmpty(nextPartition)
                && string.IsNullOrEmpty(nextRow))
                return null;

            return FormatQuery(!string.IsNullOrEmpty(filter) ? Tuple.Create("$filter", filter) : null,
                top > 0 ? Tuple.Create("$top", top.ToString(CultureInfo.InvariantCulture)) : null,
                (fields != null && fields.Length > 0) ? Tuple.Create("$select", string.Join(",", fields)) : null,
                !string.IsNullOrEmpty(nextPartition) ? Tuple.Create("NextPartitionKey", nextPartition) : null,
                !string.IsNullOrEmpty(nextRow) ? Tuple.Create("NextRowKey", nextRow) : null);
        }

        private static string FormatQuery(params Tuple<string, string>[] queryParameters)
        {
            var ret = string.Join("&", queryParameters
                .Where(p => p != null)
                .Select(p => string.Format("{0}={1}", p.Item1, Uri.EscapeDataString(p.Item2)))
                .ToArray());

            return !string.IsNullOrEmpty(ret) ? ret : null;
        }

        public string FormatResource(string partitionKey, string rowKey)
        {
            if (partitionKey == null || rowKey == null)
                return TableName;

            return string.Format("{0}(PartitionKey='{1}',RowKey='{2}')",
                TableName,
                Uri.EscapeDataString(partitionKey),
                Uri.EscapeDataString(rowKey));
        }
    }
}