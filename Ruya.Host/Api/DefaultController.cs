using System;
using System.Web.Http;

namespace Ruya.Host.Api
{
    public interface IData
    {
        Guid Id { get; set; }
    }

    public class Data : IData
    {
        public Guid Id { get; set; }
    }

    public class DefaultController : ApiController
    {
        private readonly IData _dataRepository = new Data();

        [HttpGet]
        public string Get()
        {
            return _dataRepository.Id.ToString();
        }
    }
}