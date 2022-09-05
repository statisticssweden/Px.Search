using System;
using PX.SearchAbstractions;
using PX.LuceneProvider;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Web;
using PCAxis.Menu;
using System.Xml;
using PCAxis.Paxiom.Extensions;
using PCAxis.Web.Core.Enums;
using PCAxis.Paxiom;
using System.Runtime.Caching;

namespace PX.Search
{
    
    /// <summary>
    /// Delegate function for getting the Menu
    /// </summary>
    /// <param name="database">Database id</param>
    /// <param name="nodeId">Node id</param>
    /// <param name="language">Language</param>
    /// <returns></returns>
    public delegate PCAxis.Menu.PxMenuBase GetMenuDelegate(string database, ItemSelection node, string language, out PCAxis.Menu.Item currentItem);

    /// <summary>
    /// Class for managing search indexes
    /// </summary>
    public class SearchManager
    {
        #region "Private fields"
        
        private static SearchManager _current = new SearchManager();
        private string _databaseBaseDirectory;
        private GetMenuDelegate _menuMethod;
        private static log4net.ILog _logger = log4net.LogManager.GetLogger(typeof(SearchManager));
        private FileSystemWatcher _dbConfigWatcher;
        private int _cacheTime;
        private DefaultOperator _defaultOperator;
        //private IMemoryCache _searcherCache;
        
        #endregion


        #region "Public properties"
        
        /// <summary>
        /// Get the (Singleton) SearchManager object
        /// </summary>
        public static SearchManager Current
        {
            get
            {
                return _current;
            }
        }

        /// <summary>
        /// Time in minutes that search index will be cached
        /// </summary>
        public int CacheTime 
        {
            get
            {
                return _cacheTime;
            }
            set
            {
                _cacheTime = value;
            }
        }

        #endregion

        /// <summary>
        /// Private constructor
        /// </summary>
        private SearchManager()
        {
        }

        #region "Public methods"

        /// <summary>
        /// Initialize the SearchManager
        /// </summary>
        /// <param name="databaseBaseDirectory">Base directory for PX databases</param>
        /// <param name="menuMethod">Delegate method to get the Menu</param>
        /// <param name="cacheTime">Time in minutes that searchers will be cached</param>
        public void Initialize(string databaseBaseDirectory, GetMenuDelegate menuMethod, int cacheTime=60, DefaultOperator defaultOperator=DefaultOperator.OR)
        {
            _databaseBaseDirectory = databaseBaseDirectory;
            SetDbConfigWatcher();
            _menuMethod = menuMethod;
            _cacheTime = cacheTime;
            PxModelManager.Current.Initialize(databaseBaseDirectory);
            SetDefaultOperator(defaultOperator);
            //_searcherCache = new MemoryCache(new MemoryCacheOptions());
        }


        /// <summary>
        /// Create index for the specified database and language
        /// </summary>
        /// <param name="database">Database id</param>
        /// <param name="language">language</param>
        public bool CreateIndex(string database, string language)
        {
            //Indexer indexer = new Indexer(GetIndexDirectoryPath(database, language), _menuMethod, database, language);
            IPxSearchProvider searchProvider = new LuceneSearchProvider(_databaseBaseDirectory,database,language);
            IIndexer indexer = searchProvider.GetIndexer();
            indexer.Create(true);

            try
            {
                ItemSelection node = null;

                // Get database
                PCAxis.Menu.Item itm;
                PCAxis.Menu.PxMenuBase db = _menuMethod(database, node, language, out itm);
                if (db == null)
                {
                    _logger.Error("Failed to access database '" + database + "'. Creation of search index aborted.");
                    _logger.Error("Rollback of '" + database + "' done");
                    return false;
                }

                PCAxis.Web.Core.Enums.DatabaseType dbType;
                if (db is PCAxis.Menu.Implementations.XmlMenu)
                {
                    dbType = DatabaseType.PX;
                }
                else
                {
                    dbType = DatabaseType.CNMM;
                }

                if (db.RootItem != null)
                {
                    foreach (var item in db.RootItem.SubItems)
                    {
                        if (item is PCAxis.Menu.PxMenuItem)
                        {
                            TraverseDatabase(dbType, item as PxMenuItem, indexer, "/" + item.ID.Selection, database, language);
                        }
                        else if (item is PCAxis.Menu.TableLink)
                        {
                            IndexTable(dbType, (TableLink)item, "/" + item.ID.Menu, indexer, database, language);
                        }
                    }
                }

                indexer.End();
            }
            catch (Exception e)
            {
                _logger.Error(e);
                throw;
            }
            finally {
                indexer.Dispose();  
            }
        

            RemoveSearcher(database, language);
            return true;
        }

        /// <summary>
        /// Update index for the specified database and language
        /// </summary>
        /// <param name="database">Database id</param>
        /// <param name="language">language</param>
        public bool UpdateIndex(string database, string language, List<TableUpdate> tableList)
        {
            IPxSearchProvider searchProvider = new LuceneSearchProvider(_databaseBaseDirectory,database,language);
            IIndexer indexer = searchProvider.GetIndexer();
            indexer.Create(false);

            ItemSelection node = null;
            PCAxis.Menu.Item currentTable;
            string[] pathParts;
            string title;
            string menu, selection;
            DateTime published = DateTime.MinValue;
            bool doUpdate;
            //using (IIndexer indexer = searchProvider.GetIndexer(GetIndexDirectoryPath(database, language), database, language);)
            //indexer.Create()
            try
            {
                foreach (TableUpdate table in tableList)
                {
                    doUpdate = false;
                    PXModel model = PxModelManager.Current.GetModel(DatabaseType.CNMM, database, language, table.Id);

                    // Get default value for title
                    title = model.Meta.Title;

                    // Get table title from _menuMethod
                    // table.Path is supposed to have the following format: path/path/path
                    // Example: BE/BE0101/BE0101A
                    pathParts = table.Path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                    if (pathParts.Length > 1)
                    {
                        menu = pathParts[pathParts.Length - 1];
                        selection = table.Id;

                        node = new ItemSelection(menu, selection);
                        PCAxis.Menu.PxMenuBase db = _menuMethod(database, node, language, out currentTable);

                        if (currentTable != null)
                        {
                            if (currentTable is TableLink)
                            {
                                doUpdate = true;
                                // Get table title from the menu method
                                if (!string.IsNullOrEmpty(currentTable.Text))
                                {
                                    title = currentTable.Text;
                                }
                                if (((TableLink)currentTable).Published != null)
                                {
                                    published = (DateTime)((TableLink)currentTable).Published;
                                }
                            }
                        }
                    }

                    if (doUpdate)
                    {
                        indexer.UpdatePaxiomDocument(database, table.Id, table.Path, table.Id, title, published, model.Meta);
                        _logger.Info("Search index " + database + " - " + language + " updated table " + table.Id);
                    }
                }
                indexer.End();
            }
            catch (Exception e)
            {
                _logger.Error(e);
                throw;
            }
            finally {
                indexer.Dispose();
            }
            RemoveSearcher(database, language);
            return true;
        }


        /// <summary>
        /// Search for text in the specified index 
        /// </summary>
        /// <param name="database">Database id</param>
        /// <param name="language">Language</param>
        /// <param name="text">Text to search for</param>
        /// <returns></returns>
        public List<SearchResultItem> Search(string database, string language, string text, out SearchStatusType status, string filter = "", int resultListLength = 250)
        {
            ISearcher searcher = GetSearcher(database, language);
            searcher.SetDefaultOperator(_defaultOperator);

            if (searcher == null)
            {
                // Return empty list
                status = SearchStatusType.NotIndexed;
                return new List<SearchResultItem>();
            }

            //status = SearchStatusType.Successful;
            return searcher.Search(text, filter, resultListLength, out status);
        }

        /// <summary>
        /// Set which operator AND/OR will be used by default when more than one word is specified for a search query
        /// </summary>
        /// <param name="defaultOPerator"></param>
        public void SetDefaultOperator(DefaultOperator defaultOperator)
        {
            _defaultOperator = defaultOperator;
        }

        #endregion

        #region "Private methods"

        /// <summary>
        /// Recursively traverse the database to add all tables as Document objects into the index
        /// </summary>
        /// <param name="itm">Current node in database to add Document objects for</param>
        /// <param name="writer">IndexWriter object</param>
        /// <param name="path">Path within the database for this node</param>
        private void TraverseDatabase(PCAxis.Web.Core.Enums.DatabaseType dbType, PxMenuItem itm, IIndexer indexer, string path, string database, string language)
        {
            PCAxis.Menu.Item newItem;
            PCAxis.Menu.PxMenuBase db = _menuMethod(database, itm.ID, language, out newItem);
            PxMenuItem m = (PxMenuItem)newItem;

            if (m == null)
            {
                return;
            }

            foreach (var item in m.SubItems)
            {
                if (item is PxMenuItem)
                {
                    TraverseDatabase(dbType, item as PxMenuItem, indexer, path + "/" + item.ID.Selection, database, language);
                }
                else if (item is TableLink)
                {
                    IndexTable(dbType, (TableLink)item, path, indexer, database, language);
                }
            }
        }


        /// <summary>
        /// Add table to search index
        /// </summary>
        /// <param name="dbType">Type of database</param>
        /// <param name="item">TableLink object representing the table</param>
        /// <param name="path">Path to table within database</param>
        /// <param name="writer">IndexWriter object</param>
        private void IndexTable(PCAxis.Web.Core.Enums.DatabaseType dbType, TableLink item, string path, IIndexer indexer, string database, string language)
        {
            item.ID.Selection = CleanTableId(item.ID);

            PXModel model = PxModelManager.Current.GetModel(dbType, database, language, item.ID);

            if (model != null)
            {
                string id;
                string tablePath;
                string table = "";
                string title = "";
                DateTime published = DateTime.MinValue;

                if (dbType == DatabaseType.PX)
                {
                    char[] sep = { '\\' };
                    string[] parts = item.ID.Selection.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                    StringBuilder pxPath = new StringBuilder();

                    // PX database
                    id = item.ID.Selection;

                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        if (i > 0)
                        {
                            pxPath.Append("/");
                        }
                        pxPath.Append(parts[i]);
                    }
                    tablePath = pxPath.ToString();
                    table = parts.Last();
                    title = item.Text;
                    if (((TableLink)item).Published != null)
                    {
                        published = (DateTime)((TableLink)item).Published;
                    }
                }
                else
                {
                    // CNMM database
                    id = item.ID.Selection;
                    tablePath = path;
                    table = item.ID.Selection;
                    title = item.Text;
                    if (((TableLink)item).Published != null)
                    {
                        published = (DateTime)((TableLink)item).Published;
                    }
                }
                indexer.AddPaxiomDocument(database, id, tablePath, table, title, published, model.Meta);
            }
        }

        /// <summary>
        /// Get table id without database name
        /// Example: If node.Selection = databaseid:tableid then tableid will be returned
        /// </summary>
        /// <param name="node">node representing the table</param>
        /// <returns>Table id as a string</returns>
        private string CleanTableId(ItemSelection node)
        {
            int index = node.Selection.IndexOf(":");

            if ((index > -1) && (node.Selection.Length > index))
            {
                return node.Selection.Substring(index + 1);
            }
            else
            {
                return node.Selection;
            }
        }

        /// <summary>
        /// Add file system watcher for the database.config files
        /// </summary>
        private void SetDbConfigWatcher()
        {
            string dir;
            if (Path.IsPathRooted(_databaseBaseDirectory))
            {
                dir = _databaseBaseDirectory;
            }
            else
            {
                //dir = HttpContext.Current.Server.MapPath(_databaseBaseDirectory);
                dir = _databaseBaseDirectory;
            }
            _dbConfigWatcher = new FileSystemWatcher(dir);
            _dbConfigWatcher.EnableRaisingEvents = true;
            _dbConfigWatcher.IncludeSubdirectories = true;
            _dbConfigWatcher.Filter = "database.config";
            _dbConfigWatcher.Changed += new FileSystemEventHandler(DatabaseConfigChanged);
        }

        // Event handler for when a database.config file has been changed  
        private void DatabaseConfigChanged(object source, FileSystemEventArgs e)
        {
            FileInfo dbConf = new FileInfo(e.FullPath);
            DirectoryInfo dbDir = dbConf.Directory;
            string indexPath = Path.Combine(dbDir.FullName, "_INDEX"); 

            if (!Directory.Exists(indexPath))
            {
                return;
            }

            DirectoryInfo indexDir = new DirectoryInfo(indexPath);

            
            // Check if search index has been updated
            DateTime IndexUpdated;
            if (GetIndexUpdated(e.FullPath, out IndexUpdated))
            {
                foreach (DirectoryInfo langDir in indexDir.GetDirectories())
                {
                    string key = CreateSearcherKey(dbDir.Name, langDir.Name);
                    //ISearcher searcher = (ISearcher)System.Web.Hosting.HostingEnvironment.Cache[key];
                    ISearcher searcher = (ISearcher)MemoryCache.Default.Get(key);

                    if (searcher != null)
                    {
                        if (searcher.CreationTime < IndexUpdated)
                        {
                            // Search index has been updated and our searcher is out of date - remove it!
                            RemoveSearcher(dbDir.Name, langDir.Name);
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Read the time when the search index was last updated from the specified database.config file
        /// </summary>
        /// <param name="path">Path to database.config file</param>
        /// <param name="indexUpdated">Out parameter containing the last updated date</param>
        /// <returns>True if last updated could be read from the file, else false</returns>
        private bool GetIndexUpdated(string path, out DateTime indexUpdated)
        {
            indexUpdated = DateTime.MinValue;

            try
            {
                XmlDocument xdoc = new XmlDocument();
                xdoc.Load(path);
                XmlNode node = xdoc.SelectSingleNode("/settings/searchIndex/indexUpdated");

                if (node.InnerText.IsPxDate())
                {
                    indexUpdated = node.InnerText.PxDateStringToDateTime();
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Get Searcher from cache
        /// </summary>
        /// <param name="database">database</param>
        /// <param name="language">language</param>
        /// <returns></returns>
        public ISearcher GetSearcher(string database, string language)
        {
            
            string key = CreateSearcherKey(database, language);

            //if (System.Web.Hosting.HostingEnvironment.Cache[key] == null)
            if (MemoryCache.Default.Get(key) == null)
            {
                IPxSearchProvider searchProvider = new LuceneSearchProvider(_databaseBaseDirectory,database,language);
                // Create new Searcher and add to cache
                ISearcher searcher = searchProvider.GetSearcher();

                // Add searcher to cache for 5 minutes
                //System.Web.Hosting.HostingEnvironment.Cache.Insert(key, searcher, null, DateTime.Now.AddMinutes(_cacheTime), System.Web.Caching.Cache.NoSlidingExpiration);
                MemoryCache.Default.Set(key,searcher,DateTime.Now.AddMinutes(_cacheTime));
            }

            // Get from cache
            //return (ISearcher)System.Web.Hosting.HostingEnvironment.Cache[key];
            return (ISearcher)MemoryCache.Default.Get(key);
        }

        /// <summary>
        /// Remove the specified searcher from cache
        /// </summary>
        /// <param name="database">database</param>
        /// <param name="language">language</param>
        private void RemoveSearcher(string database, string language)
        {
            
            string key = CreateSearcherKey(database, language);

            /*if (System.Web.Hosting.HostingEnvironment.Cache[key] != null)
            {
                System.Web.Hosting.HostingEnvironment.Cache.Remove(key);
            }*/
            /*
            if (_searcherCache.Get(key) != null)
            {
               _searcherCache.Remove(key);
            }
            */
            if (MemoryCache.Default.Get(key) != null)
            {
                MemoryCache.Default.Remove(key);
            }

        }

        /// <summary>
        /// Create cache Searcher key
        /// </summary>
        /// <param name="database"></param>
        /// <param name="language"></param>
        /// <returns></returns>
        private string CreateSearcherKey(string database, string language)
        {
            StringBuilder key = new StringBuilder();

            key.Append("px-search-");
            key.Append(database);
            key.Append("|");
            key.Append(language);

            return key.ToString();
        }

        #endregion
    }
}
