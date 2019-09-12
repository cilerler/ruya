using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Ruya.Data.ThirdParty;

namespace Ruya.Data
{
    // TEST class DataTableHelper
    public static class DataTableHelper
    {
        /// <summary>
        ///     Creates a <see cref="DataTable" /> that contains the data from a source sequence.
        /// </summary>
        /// <remarks>
        ///     The initial schema of the DataTable is based on schema of the type T. All public property and fields are turned
        ///     into DataColumns.
        ///     If the source sequence contains a sub-type of T, the table is automatically expanded for any addition public
        ///     properties or fields.
        /// </remarks>
        public static DataTable ToDataTable<T>(this IEnumerable<T> source)
        {
            return new ObjectShredder<T>().Shred(source, null, null);
        }

        /// <summary>
        ///     Loads the data from a source sequence into an existing <see cref="DataTable" />.
        /// </summary>
        /// <remarks>
        ///     The schema of <paramref name="table" /> must be consistent with the schema of the type T (all public property and
        ///     fields are mapped to DataColumns).
        ///     If the source sequence contains a sub-type of T, the table is automatically expanded for any addition public
        ///     properties or fields.
        /// </remarks>
        public static DataTable ToDataTable<T>(this IEnumerable<T> source, DataTable table, LoadOption? options)
        {
            return new ObjectShredder<T>().Shred(source, table, options);
        }

        // COMMENT method Sort
        public static DataTable Sort(this DataTable dataTable, string columnKey, bool isAscending, bool persist)
        {
            // HARD-CODED constant
            const string sortFormat = "{0} {1}";
            string sortDirection = isAscending
                                       ? "ASC"
                                       : "DESC";
            dataTable.DefaultView.Sort = string.Format(sortFormat, columnKey, sortDirection);
            if (persist)
            {
                DataTable dtSorted = dataTable.DefaultView.ToTable();
                dataTable = dtSorted;
            }
            return dataTable;
        }

#warning Refactor
        // COMMENT method ToCommaSeparatedList
        // TEST method ToCommaSeparatedList
        public static List<string> ToCommaSeparatedList(this DataTable source, bool sort, bool header)
        {
            var result = new List<string>();

            // get Headers
            List<string> columnHeaders = (from DataColumn column in source.Columns
                                          select column.ColumnName).ToList();

            // sort Columns
            if (sort) { columnHeaders.Sort(); }

            // add Headers
            // HARD-CODED constant
            const char unsecureDelimiter = ',';
            const char securedDelimiter = ';';
            if (header)
            {
                // HARD-CODED constant
                string result1 = columnHeaders.Aggregate(string.Empty, (current, columnHeader) => string.Format("{0}{1}{2}", current, columnHeader.Replace(unsecureDelimiter, securedDelimiter), unsecureDelimiter));
                result.Add(result1.TrimEnd(unsecureDelimiter));
            }

            // add Rows
            result.AddRange(from DataRow row in source.Rows                                
                            select columnHeaders.Select(columnHeader => (row[columnHeader] ?? string.Empty).ToString().Replace(unsecureDelimiter, securedDelimiter))
                                            // HARD-CODED constant
                                                .Aggregate(string.Empty, (current, cellValue) => string.Format("{0}{1}{2}", current, cellValue, unsecureDelimiter))
                                                .TrimEnd(unsecureDelimiter));

            return result;
        }
    }
}