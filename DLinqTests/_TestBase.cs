using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DLinqIntegrationTests
{
    [TestClass]
    public class _TestBase
    {
        private IConfiguration _config;

        public _TestBase()
        {
            _config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory) // ensures correct path
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
                .Build();
        }

        [TestMethod]
        public void ReadConnectionStrings()
        {
            string postgresConnection = _config["ConnectionStrings:postgres"];
            string sqlServerConnection = _config["ConnectionStrings:sqlserver"];
            Assert.IsNotNull(postgresConnection);
            Assert.IsNotNull(sqlServerConnection);
            Console.WriteLine($"Postgres connection string: {postgresConnection}");
            Console.WriteLine($"SQL Server connection string: {sqlServerConnection}");
        }
    }
}