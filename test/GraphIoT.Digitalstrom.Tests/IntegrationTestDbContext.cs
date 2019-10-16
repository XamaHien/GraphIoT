using Microsoft.EntityFrameworkCore;
using PhilipDaubmeier.GraphIoT.Digitalstrom.Database;
using PhilipDaubmeier.TokenStore.Database;

namespace PhilipDaubmeier.DigitalstromTimeSeriesApi.Database
{
    public class IntegrationTestDbContext : DbContext, ITokenStoreDbContext, IDigitalstromDbContext
    {
        #region ITokenStoreDbContext
        public DbSet<AuthData> AuthDataSet { get; set; }
        #endregion

        #region IDigitalstromDbContext
        public DbSet<DigitalstromZone> DsZones { get; set; }

        public DbSet<DigitalstromCircuit> DsCircuits { get; set; }

        public DbSet<DigitalstromZoneSensorLowresData> DsSensorLowresDataSet { get; set; }

        public DbSet<DigitalstromZoneSensorMidresData> DsSensorDataSet { get; set; }

        public DbSet<DigitalstromSceneEventData> DsSceneEventDataSet { get; set; }

        public DbSet<DigitalstromEnergyLowresData> DsEnergyLowresDataSet { get; set; }

        public DbSet<DigitalstromEnergyMidresData> DsEnergyMidresDataSet { get; set; }

        public DbSet<DigitalstromEnergyHighresData> DsEnergyHighresDataSet { get; set; }
        #endregion

        public IntegrationTestDbContext(DbContextOptions<IntegrationTestDbContext> options)
            : base(options)
        { }
    }
}