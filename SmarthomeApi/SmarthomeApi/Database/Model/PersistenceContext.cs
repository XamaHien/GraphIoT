﻿using Microsoft.EntityFrameworkCore;

namespace SmarthomeApi.Database.Model
{
    public class PersistenceContext : DbContext
    {
        public DbSet<AuthData> AuthDataSet { get; set; }

        public DbSet<Calendar> Calendars { get; set; }

        public DbSet<CalendarAppointment> CalendarAppointments { get; set; }

        public DbSet<CalendarOccurence> CalendarOccurances { get; set; }

        public PersistenceContext(DbContextOptions<PersistenceContext> options)
            : base(options)
        { }
    }
}
