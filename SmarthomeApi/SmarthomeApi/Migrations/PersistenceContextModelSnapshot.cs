﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmarthomeApi.Database.Model;

namespace SmarthomeApi.Migrations
{
    [DbContext(typeof(PersistenceContext))]
    partial class PersistenceContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "2.2.1-servicing-10028")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("SmarthomeApi.Database.Model.AuthData", b =>
                {
                    b.Property<string>("AuthDataId")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("DataContent");

                    b.HasKey("AuthDataId");

                    b.ToTable("AuthDataSet");
                });

            modelBuilder.Entity("SmarthomeApi.Database.Model.Calendar", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Owner");

                    b.HasKey("Id");

                    b.ToTable("Calendars");
                });

            modelBuilder.Entity("SmarthomeApi.Database.Model.CalendarEntry", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int>("BusyState");

                    b.Property<Guid>("CalendarId");

                    b.Property<DateTime>("EndTime");

                    b.Property<bool>("IsFullDay");

                    b.Property<bool>("IsPrivate");

                    b.Property<DateTime>("Modified");

                    b.Property<string>("RecurranceRule")
                        .IsRequired()
                        .HasMaxLength(255);

                    b.Property<DateTime>("StartTime");

                    b.Property<string>("Summary");

                    b.HasKey("Id");

                    b.HasIndex("CalendarId");

                    b.ToTable("CalendarEntry");
                });

            modelBuilder.Entity("SmarthomeApi.Database.Model.CalendarEntry", b =>
                {
                    b.HasOne("SmarthomeApi.Database.Model.Calendar", "Calendar")
                        .WithMany("Entries")
                        .HasForeignKey("CalendarId")
                        .OnDelete(DeleteBehavior.Cascade);
                });
#pragma warning restore 612, 618
        }
    }
}
