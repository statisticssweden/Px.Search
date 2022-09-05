using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PX.SearchAbstractions;

namespace PX.Search
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
