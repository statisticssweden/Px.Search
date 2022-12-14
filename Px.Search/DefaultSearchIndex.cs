using Px.Search.Abstractions;
using System;
using System.Collections.Generic;

namespace Px.Search
{
    /// <summary>
    /// Default implementation of SearchIndex class
    /// </summary>
    public class DefaultSearchIndex : ISearchIndex
    {
        public List<TableUpdate> GetUpdatedTables(DateTime dateFrom, string database, string language)
        {
            List<TableUpdate> lst = new List<TableUpdate>();

            // Return empty list
            return lst;
        }
    }
}
