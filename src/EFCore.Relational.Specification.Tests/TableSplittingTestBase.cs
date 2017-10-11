// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestModels.TransportationModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// ReSharper disable InconsistentNaming
namespace Microsoft.EntityFrameworkCore
{
    public abstract class TableSplittingTestBase
    {
        [Fact(Skip = "#8973")]
        public virtual void Can_query_shared()
        {
            using (CreateTestStore(OnModelCreating))
            {
                using (var context = CreateContext())
                {
                    Assert.Equal(4, context.Set<Operator>().ToList().Count);
                }
            }
        }

        [Fact(Skip = "#8973")]
        public virtual void Can_query_shared_derived()
        {
            using (CreateTestStore(OnModelCreating))
            {
                using (var context = CreateContext())
                {
                    Assert.Equal(1, context.Set<FuelTank>().ToList().Count);
                }
            }
        }

        [Fact(Skip = "#8973")]
        public virtual void Can_use_with_redundant_relationships()
        {
            Test_roundtrip(OnModelCreating);
        }

        [Fact(Skip = "#8973")]
        public virtual void Can_use_with_chained_relationships()
        {
            Test_roundtrip(
                modelBuilder =>
                    {
                        OnModelCreating(modelBuilder);
                        modelBuilder.Entity<FuelTank>(eb => { eb.Ignore(e => e.Vehicle); });
                    });
        }

        [Fact(Skip = "#8973")]
        public virtual void Can_use_with_fanned_relationships()
        {
            Test_roundtrip(
                modelBuilder =>
                    {
                        OnModelCreating(modelBuilder);
                        modelBuilder.Entity<FuelTank>(eb => { eb.Ignore(e => e.Engine); });
                        modelBuilder.Entity<CombustionEngine>(eb => { eb.Ignore(e => e.FuelTank); });
                    });
        }

        protected void Test_roundtrip(Action<ModelBuilder> onModelCreating)
        {
            using (CreateTestStore(onModelCreating))
            {
                using (var context = CreateContext())
                {
                    context.AssertSeeded();
                }
            }
        }

        [Fact]
        public virtual void Can_change_dependent_instance_non_derived()
        {
            using (CreateTestStore(modelBuilder =>
                {
                    OnModelCreating(modelBuilder);
                    modelBuilder.Entity<Engine>().ToTable("Engines");
                    modelBuilder.Entity<FuelTank>(
                        eb =>
                            {
                                eb.ToTable("FuelTanks");
                                eb.HasOne(e => e.Engine)
                                    .WithOne(e => e.FuelTank)
                                    .HasForeignKey<FuelTank>(e => e.VehicleName)
                                    .OnDelete(DeleteBehavior.Restrict);
                            });
                }))
            {
                using (var context = CreateContext())
                {
                    context.AssertSeeded();

                    var bike = context.Vehicles.Include(v => v.Operator).Single(v => v.Name == "Trek Pro Fit Madone 6 Series");

                    bike.Operator = new Operator { Name = "repairman" };

                    context.ChangeTracker.DetectChanges();

                    bike.Operator = new Operator { Name = "Chris Horner" };

                    context.SaveChanges();
                }

                using (var context = CreateContext())
                {
                    var bike = context.Vehicles.Include(v => v.Operator).Single(v => v.Name == "Trek Pro Fit Madone 6 Series");
                    Assert.Equal("Chris Horner", bike.Operator.Name);

                    context.SaveChanges();
                }
            }
        }

        protected readonly string DatabaseName = "TableSplittingTest";
        protected TestStore TestStore { get; set; }
        protected abstract ITestStoreFactory TestStoreFactory { get; }
        protected IServiceProvider ServiceProvider { get; set; }

        protected virtual void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Vehicle>(
                eb =>
                    {
                        eb.HasDiscriminator<string>("Discriminator");
                        eb.Property<string>("Discriminator").HasColumnName("Discriminator");
                        eb.ToTable("Vehicles");
                    });

            modelBuilder.Entity<Engine>().ToTable("Vehicles");
            modelBuilder.Entity<Operator>().ToTable("Vehicles");
            modelBuilder.Entity<FuelTank>().ToTable("Vehicles");
        }

        protected TestStore CreateTestStore(Action<ModelBuilder> onModelCreating)
        {
            TestStore = TestStoreFactory.GetOrCreate(DatabaseName);

            ServiceProvider = TestStoreFactory.AddProviderServices(new ServiceCollection())
                .AddSingleton(TestModelSource.GetFactory(onModelCreating))
                .BuildServiceProvider(validateScopes: true);

            TestStore.Initialize(ServiceProvider, CreateContext, c => ((TransportationContext)c).Seed());

            return TestStore;
        }

        protected virtual DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => builder
                .EnableSensitiveDataLogging()
                .ConfigureWarnings(
                    b => b.Default(WarningBehavior.Throw)
                        .Log(CoreEventId.SensitiveDataLoggingEnabledWarning)
                        .Log(CoreEventId.PossibleUnintendedReferenceComparisonWarning));

        protected virtual TransportationContext CreateContext()
        {
            var options = AddOptions(TestStore.AddProviderOptions(new DbContextOptionsBuilder()))
                .UseInternalServiceProvider(ServiceProvider).Options;
            return new TransportationContext(options);
        }
    }
}
