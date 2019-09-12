using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Entity.Core.Metadata.Edm;
using System.Data.Entity.Core.Objects;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Ruya.Core;
using Ruya.Data.Entity.Interfaces;
using Ruya.Diagnostics;

namespace Ruya.Data.Entity
{
    public class Repository<T> : IRepository<T>
        where T : class
    {
        protected readonly ObjectSet<T> ObjSet;

        //x protected ObjectSet<T> ObjSet;
        //x protected readonly ObjectContext Context;

        public Repository(ObjectContext context)
        {
            if (context == null)
            {
                // HARD-CODED constant
                throw new DataEntityException(TraceEventType.Critical, "Repository context is null!");
            }
            ObjSet = context.CreateObjectSet<T>();
        }

        #region IRepository<T> Members

        public IQueryable<T> Find(Expression<Func<T, bool>> predicate)
        {
            IQueryable<T> output;
            try
            {
                output = ObjSet.Where(predicate);
            }
            catch (ArgumentNullException ane)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Critical, 0, ane.Message);
                output = null;
            }
            catch (Exception ex)
            {

                Tracer.Instance.TraceEvent(TraceEventType.Critical, 0,  "Can't call object set! {0}", ex.Message);

                output = null;
            }			
            return output;
        }

        public void Add(T entity)
        {
            if (!Validate(entity)
                     .Any())
            {
                ObjSet.AddObject(entity);
            }
        }

        public void Remove(T entity)
        {
            ObjSet.DeleteObject(entity);
        }

        public IEnumerable<string> Validate(T entity)
        {
            return ValidateEntity(entity);
        }


        public IQueryable<T> FindAll()
        {
            return ObjSet;
        }

        public T First(Expression<Func<T, bool>> predicate)
        {
            return ObjSet.First(predicate);
        }
        #endregion

        #region Validate

        private IEnumerable<string> ValidateEntity(object entity)
        {
            Dictionary<string, int> restrictedElements = GetElementsLength(ObjSet.Context);

            var activeElements = new List<string>();
            string tableName = entity.GetType()
                                     .Name;
            foreach (PropertyInfo e1 in entity.GetType()
                                              .GetProperties())
            {
                string propertyName = e1.Name;
                // HARD-CODED constant
                string key = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}", tableName, ControlChars.BackSlash, propertyName);

                object e1Value = e1.GetValue(entity);

                int value;
                if (restrictedElements.TryGetValue(key, out value))
                {
                    var s = e1Value as string;
                    if (s != null)
                    {
                        // HARD-CODED constant
                        string message = string.Format(CultureInfo.InvariantCulture, "{0} as {3} [{1}:{2}]", key, value, s.Length, s);
                        if (s.Length > value)
                        {
                            activeElements.Add(message);
                        }
                    }
                }

                if (!e1.PropertyType.GenericTypeArguments.Any())
                {
                    continue;
                }
                var z3 = e1Value as IEnumerable;
                if (z3 == null)
                {
                    continue;
                }
                foreach (object titem in z3)
                {
                    activeElements.AddRange(GetElementsProperty(titem, restrictedElements));
                }
            }

            if (activeElements.Any())
            {
                Tracer.Instance.TraceData(TraceEventType.Error, 0, string.Join(";", activeElements));
            }
            return activeElements;
        }

        private static IEnumerable<string> GetElementsProperty(object entity, Dictionary<string, int> restrictedElements)
        {
            var activeElements = new List<string>();
            string tableName = entity.GetType()
                                     .Name;
            foreach (PropertyInfo e1 in entity.GetType()
                                              .GetProperties())
            {
                string propertyName = e1.Name;
                // HARD-CODED constant
                string key = string.Format(CultureInfo.InvariantCulture, "{0}\\{1}", tableName, propertyName);

                object e1Value = e1.GetValue(entity);

                int value;
                if (restrictedElements.TryGetValue(key, out value))
                {
                    var s = e1Value as string;
                    if (s != null)
                    {
                        // HARD-CODED constant
                        string message = string.Format(CultureInfo.InvariantCulture, "{0} as {3} [{1}:{2}]", key, value, s.Length, s);
                        if (s.Length > value)
                        {
                            activeElements.Add(message);
                        }
                    }
                }

                if (e1.PropertyType.GenericTypeArguments.Any())
                {
                    var z3 = e1Value as IEnumerable;
                    if (z3 != null)
                    {
                        foreach (object titem in z3)
                        {
                            activeElements.AddRange(GetElementsProperty(titem, restrictedElements));
                        }
                    }
                }
            }
            return activeElements;
        }

        private static Dictionary<string, int> GetElementsLength(ObjectContext context)
        {
            var output = new Dictionary<string, int>();

            IEnumerable<GlobalItem> tables = context.MetadataWorkspace.GetItems(DataSpace.CSpace)
                                                    .Where(m => m.BuiltInTypeKind == BuiltInTypeKind.EntityType);
            foreach (GlobalItem globalItem in tables)
            {
                var table = (EntityType)globalItem;
                //x var properties = table.Properties.Where(p => p.TypeUsage.EdmType.Name == "String");
                IEnumerable<EdmProperty> properties = table.Properties.Where(p => p.TypeName == typeof(string).Name);
                foreach (EdmProperty property in properties)
                {
                    //x object maxLength = property.TypeUsage.Facets["MaxLength"].Value;
                    if (!property.IsMaxLength)
                    {
                        // HARD-CODED constant
                        string key = string.Format(CultureInfo.InvariantCulture, "{0}\\{1}", table.Name, property.Name);
                        int value = property.MaxLength ?? -1;
                        output.Add(key, value);
                    }
                }
            }

            return output;
        }

        #endregion
    }
}