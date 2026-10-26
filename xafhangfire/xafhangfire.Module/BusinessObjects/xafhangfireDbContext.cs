using DevExpress.ExpressApp.Design;
using DevExpress.ExpressApp.EFCore.DesignTime;
using DevExpress.ExpressApp.EFCore.Updating;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.EntityFrameworkCore;
namespace xafhangfire.Module.BusinessObjects
{
    [TypesInfoInitializer(typeof(DbContextTypesInfoInitializer<xafhangfireEFCoreDbContext>))]
    public class xafhangfireEFCoreDbContext : DbContext
    {
        public xafhangfireEFCoreDbContext(DbContextOptions<xafhangfireEFCoreDbContext> options) : base(options)
        {
        }
        //public DbSet<ModuleInfo> ModulesInfo { get; set; }
        public DbSet<ModelDifference> ModelDifferences { get; set; }
        public DbSet<ModelDifferenceAspect> ModelDifferenceAspects { get; set; }
        public DbSet<PermissionPolicyRole> Roles { get; set; }
        public DbSet<xafhangfire.Module.BusinessObjects.ApplicationUser> Users { get; set; }
        public DbSet<xafhangfire.Module.BusinessObjects.ApplicationUserLoginInfo> UserLoginsInfo { get; set; }
        public DbSet<FileData> FileData { get; set; }
        public DbSet<ReportDataV2> ReportDataV2 { get; set; }
        public DbSet<HCategory> HCategories { get; set; }
        public DbSet<JobDefinition> JobDefinitions { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectTask> ProjectTasks { get; set; }
        public DbSet<EmailTemplate> EmailTemplates { get; set; }
        public DbSet<JobExecutionRecord> JobExecutionRecords { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseDeferredDeletion(this);
            modelBuilder.UseOptimisticLock();
            modelBuilder.SetOneToManyAssociationDeleteBehavior(DeleteBehavior.SetNull, DeleteBehavior.Cascade);
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
            modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
            modelBuilder.Entity<xafhangfire.Module.BusinessObjects.ApplicationUserLoginInfo>(b =>
            {
                b.HasIndex(nameof(DevExpress.ExpressApp.Security.ISecurityUserLoginInfo.LoginProviderName), nameof(DevExpress.ExpressApp.Security.ISecurityUserLoginInfo.ProviderUserKey)).IsUnique();
            });
            modelBuilder.Entity<ModelDifference>()
                .HasMany(t => t.Aspects)
                .WithOne(t => t.Owner)
                .OnDelete(DeleteBehavior.Cascade);
        }

        // UPG-001/JOB-013: no Npgsql legacy timestamp switch. DateTime columns stay `timestamp without time zone`
        // and keep the wall-clock value as written (what the legacy switch did); Kind is dropped on the way in, so
        // Utc, Local and Unspecified values and LINQ parameters are all accepted.
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp without time zone").HaveConversion<UnspecifiedKindConverter>();
            configurationBuilder.Properties<DateTime?>().HaveColumnType("timestamp without time zone").HaveConversion<UnspecifiedKindConverter>();
        }
    }

    public class UnspecifiedKindConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
    {
        public UnspecifiedKindConverter() : base(v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified), v => v) { }
    }

    // This factory creates DbContext for design-time services (Model Editor, database migrations).
    public class xafhangfireDesignTimeDbContextFactory : DesignTimeDbContextFactory<xafhangfireEFCoreDbContext>
    {
        protected override string ConnectionString =>
            "EFCoreProvider=PostgreSql;Host=localhost;Port=5433;Database=xafhangfire;Username=xafhangfire;Password=xafhangfire";
    }
}
