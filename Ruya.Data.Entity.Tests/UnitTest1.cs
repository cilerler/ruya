using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.Data.Entity.Tests
{
#warning Refactor
    [TestClass]
    public class UnitTest1
    {
        protected IList<Case> CaseData;
        protected InMemoryUnitOfWork UnitOfWork;

        public UnitTest1()
        {
            CaseData = ObjectMother.CreateCases()
                                   .ToList();
            UnitOfWork = new InMemoryUnitOfWork
                         {
                             Case = new InMemoryObjectSet<Case>(CaseData)
                         };
        }

        [TestMethod]
        public void TestMethod1()
        {
        }
    }


    [TestClass]
    public class UnitTest2
    {
        private readonly Configuration _configuration;
        protected InMemoryUnitOfWork UnitOfWork;

        public UnitTest2()
        {
            _configuration = Configuration.GetConfiguration(false);
            UnitOfWork = new InMemoryUnitOfWork(Configuration.ConnectionSettings.EntityConnectionString.ToString());
        }

        [TestMethod]
        public void TestMethod1()
        {
        }
    }

    [TestClass]
    public class UnitTest3
    {
        private readonly Configuration _configuration;
        protected InMemoryUnitOfWork UnitOfWork;

        public UnitTest3()
        {
            _configuration = Configuration.GetConfiguration(false);
            UnitOfWork = new InMemoryUnitOfWork(Configuration.ConnectionSettings.EntityConnectionString.ToString());
        }

        [TestMethod]
        public void TestMethod1()
        {
            var myCase = PopulateData<Case>(_configuration, "0050001SC");
            //x var myCase = PopulateData<Case>(_configuration, "0050001UB");
            //UnitOfWork.Case.Add(myCase);
            UnitOfWork.Commit();
        }

        private static T PopulateData<T>(Configuration configuration, string reportId) where T : new()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), reportId + ".json");

            T myCase;
            try
            {
                /*
                var reportMaker = new ReportMaker(new VirtualPartnerReportBuilder(ReportType.Data, configuration.AgencyName, reportId, configuration.SchemaExtras));
                reportMaker.BuildReport();
                if (string.IsNullOrWhiteSpace(reportMaker.GetReport().Content)) throw new ArgumentNullException();
                
                File.WriteAllText(path, reportMaker.GetReport().Content);
                var input = reportMaker.GetReport()
                                       .Content;
                 */
                /*
               var input = File.ReadAllText(path);

               myCase = new T();
               JObject sqlCompatibleData = JObject.Parse(input);
               var jProperty = sqlCompatibleData.First as JProperty;
               if (jProperty != null)
               {
                   string insertableData = jProperty.Value.First.ToString();
                   try
                   {                       
                       JsonConvert.PopulateObject(insertableData, myCase);
                   }
                   catch (Exception)
                   {
                       myCase = default(T);
                   }
               }
               */
                throw new NotImplementedException();
            }
            catch (Exception)
            {
                myCase = default(T);
            }
            return myCase;
        }
    }

    public class Configuration
    {
        public static Configuration GetConfiguration(bool b)
        {
            throw new NotImplementedException();
        }

        #region Nested type: ConnectionSettings

        public static class ConnectionSettings
        {
            public static object EntityConnectionString { get; set; }
        }

        #endregion
    }
}