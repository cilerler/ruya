using System;
using System.Data.Entity.Core;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ruya.Core;
using Ruya.Data.Entity.Interfaces;
using Ruya.Data.Entity.Properties;
using Ruya.Diagnostics;

namespace Ruya.Data.Entity
{
    public class UnitOfWork : IUnitOfWork, IDisposable
    {
        private readonly ObjectContext _context;
        private bool _disposed;

        /*
        private Repository<Case> _case;
        private Repository<FieldReport> _fieldReport;
        public IRepository<Case> Case => _case ?? (_case = new Repository<Case>(_context));
        public IRepository<FieldReport> FieldReport => _fieldReport ?? (_fieldReport = new Repository<FieldReport>(_context));
        */

        public UnitOfWork(string connectionString)
        {
            _context = new ObjectContext(connectionString);
            _context.ContextOptions.LazyLoadingEnabled = true;
        }

        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
            // ReSharper disable GCSuppressFinalizeForTypeWithoutDestructor
            GC.SuppressFinalize(this);
            // ReSharper restore GCSuppressFinalizeForTypeWithoutDestructor
        }

        #endregion

        #region IUnitOfWork Members

        public void Commit()
        {
            try
            {
                _context.SaveChanges();
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, Resources.UnitOfWork_Commit_SaveChangesCompleted);
            }
            catch (DbEntityValidationException dve)
            {
                string message = DbEntityValidationExceptionHandler(dve);

                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, message);
            }
            catch (UpdateException uex)
            {
                var sqlException = (SqlException) uex.InnerException;

                foreach (SqlError error in sqlException.Errors)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, string.Format(CultureInfo.InvariantCulture, Resources.UnitOfWork_Commit_UpdateException, error.Message));
                }
            }
            catch (Exception ex)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ex.Message);
            }
        }

        #endregion

        private static string DbEntityValidationExceptionHandler(DbEntityValidationException dve)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(Resources.UnitOfWork_DbEntityValidationExceptionHandler_EntityValidationFailed);
            foreach (DbEntityValidationResult failure in dve.EntityValidationErrors)
            {
                stringBuilder.AppendFormat(CultureInfo.InvariantCulture, Resources.UnitOfWork_DbEntityValidationExceptionHandler_FailedValidation, failure.Entry.Entity.GetType(), ControlChars.CarriageReturnLineFeed);
                foreach (DbValidationError error in failure.ValidationErrors)
                {
                    // HARD-CODED constant
                    stringBuilder.AppendFormat(CultureInfo.InvariantCulture, "- {0} : {1}", error.PropertyName, error.ErrorMessage);
                    stringBuilder.AppendLine();
                }
            }
            return stringBuilder.ToString();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _context.Dispose();
            }

            _disposed = true;
        }
    }
}