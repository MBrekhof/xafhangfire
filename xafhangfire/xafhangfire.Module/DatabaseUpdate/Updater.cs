using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.EF;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.Extensions.DependencyInjection;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Module.DatabaseUpdate
{
    // For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Updating.ModuleUpdater
    public class Updater : ModuleUpdater
    {
        public Updater(IObjectSpace objectSpace, Version currentDBVersion) :
            base(objectSpace, currentDBVersion)
        {
        }
        public override void UpdateDatabaseAfterUpdateSchema()
        {
            base.UpdateDatabaseAfterUpdateSchema();
            //string name = "MyName";
            //EntityObject1 theObject = ObjectSpace.FirstOrDefault<EntityObject1>(u => u.Name == name);
            //if(theObject == null) {
            //    theObject = ObjectSpace.CreateObject<EntityObject1>();
            //    theObject.Name = name;
            //}

            // The code below creates users and roles for testing purposes only.
            // In production code, you can create users and assign roles to them automatically, as described in the following help topic:
            // https://docs.devexpress.com/eXpressAppFramework/119064/data-security-and-safety/security-system/authentication
#if !RELEASE
            // If a role doesn't exist in the database, create this role
            var defaultRole = CreateDefaultRole();
            var adminRole = CreateAdminRole();
            var backgroundJobsRole = CreateBackgroundJobsRole();

            ObjectSpace.CommitChanges(); //This line persists created object(s).

            UserManager userManager = ObjectSpace.ServiceProvider.GetRequiredService<UserManager>();

            // If a user named 'User' doesn't exist in the database, create this user
            if (userManager.FindUserByName<ApplicationUser>(ObjectSpace, "User") == null)
            {
                // Set a password if the standard authentication type is used
                string EmptyPassword = "";
                _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "User", EmptyPassword, (user) =>
                {
                    // Add the Users role to the user
                    user.Roles.Add(defaultRole);
                });
            }

            // If a user named 'Admin' doesn't exist in the database, create this user
            if (userManager.FindUserByName<ApplicationUser>(ObjectSpace, "Admin") == null)
            {
                // Set a password if the standard authentication type is used
                string EmptyPassword = "";
                _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "Admin", EmptyPassword, (user) =>
                {
                    // Add the Administrators role to the user
                    user.Roles.Add(adminRole);
                });
            }

            // Service user for background Hangfire jobs (read-only access)
            if (userManager.FindUserByName<ApplicationUser>(ObjectSpace, "HangfireJob") == null)
            {
                string EmptyPassword = "";
                _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "HangfireJob", EmptyPassword, (user) =>
                {
                    user.Roles.Add(backgroundJobsRole);
                });
            }

            ObjectSpace.CommitChanges(); //This line persists created object(s).

            SeedJobDefinitions();
            ObjectSpace.CommitChanges();

            SeedCrmData();
            ObjectSpace.CommitChanges();

            SeedEmailTemplates();
            ObjectSpace.CommitChanges();
#endif
        }
        public override void UpdateDatabaseBeforeUpdateSchema()
        {
            base.UpdateDatabaseBeforeUpdateSchema();
        }
        PermissionPolicyRole CreateAdminRole()
        {
            PermissionPolicyRole adminRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == "Administrators");
            if (adminRole == null)
            {
                adminRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
                adminRole.Name = "Administrators";
                adminRole.IsAdministrative = true;
            }
            return adminRole;
        }
        void SeedJobDefinitions()
        {
            if (ObjectSpace.FirstOrDefault<JobDefinition>(j => j.Name == "Demo Log Job") == null)
            {
                var demoJob = ObjectSpace.CreateObject<JobDefinition>();
                demoJob.Name = "Demo Log Job";
                demoJob.JobTypeName = "DemoLogCommand";
                demoJob.ParametersJson = "{\"Message\":\"Hello from seed data\",\"DelaySeconds\":2}";
                demoJob.IsEnabled = true;
            }

            if (ObjectSpace.FirstOrDefault<JobDefinition>(j => j.Name == "List Users Job") == null)
            {
                var listJob = ObjectSpace.CreateObject<JobDefinition>();
                listJob.Name = "List Users Job";
                listJob.JobTypeName = "ListUsersCommand";
                listJob.ParametersJson = "{\"MaxResults\":5}";
                listJob.IsEnabled = true;
            }

            if (ObjectSpace.FirstOrDefault<JobDefinition>(j => j.Name == "Scheduled Demo (Every 5 Min)") == null)
            {
                var scheduledJob = ObjectSpace.CreateObject<JobDefinition>();
                scheduledJob.Name = "Scheduled Demo (Every 5 Min)";
                scheduledJob.JobTypeName = "DemoLogCommand";
                scheduledJob.ParametersJson = "{\"Message\":\"Scheduled run\",\"DelaySeconds\":1}";
                scheduledJob.CronExpression = "*/5 * * * *";
                scheduledJob.IsEnabled = true;
            }

            if (ObjectSpace.FirstOrDefault<JobDefinition>(j => j.Name == "Generate Project Status Report") == null)
            {
                var projectReportJob = ObjectSpace.CreateObject<JobDefinition>();
                projectReportJob.Name = "Generate Project Status Report";
                projectReportJob.JobTypeName = "GenerateReportCommand";
                projectReportJob.ParametersJson = "{\"ReportName\":\"Project Status Report\",\"OutputFormat\":\"Pdf\"}";
                projectReportJob.IsEnabled = true;
            }

            if (ObjectSpace.FirstOrDefault<JobDefinition>(j => j.Name == "Generate Contact List Report") == null)
            {
                var contactReportJob = ObjectSpace.CreateObject<JobDefinition>();
                contactReportJob.Name = "Generate Contact List Report";
                contactReportJob.JobTypeName = "GenerateReportCommand";
                contactReportJob.ParametersJson = "{\"ReportName\":\"Contact List by Organization\",\"OutputFormat\":\"Pdf\"}";
                contactReportJob.IsEnabled = true;
            }

            if (ObjectSpace.FirstOrDefault<JobDefinition>(j => j.Name == "Welcome Mail Merge") == null)
            {
                var mailMergeJob = ObjectSpace.CreateObject<JobDefinition>();
                mailMergeJob.Name = "Welcome Mail Merge";
                mailMergeJob.JobTypeName = "SendMailMergeCommand";
                mailMergeJob.ParametersJson = "{\"TemplateName\":\"Welcome Contact\"}";
                mailMergeJob.IsEnabled = true;
            }

            if (ObjectSpace.FirstOrDefault<JobDefinition>(j => j.Name == "Email Project Status Report") == null)
            {
                var reportEmailJob = ObjectSpace.CreateObject<JobDefinition>();
                reportEmailJob.Name = "Email Project Status Report";
                reportEmailJob.JobTypeName = "SendReportEmailCommand";
                reportEmailJob.ParametersJson = "{\"ReportName\":\"Project Status Report\",\"Recipients\":\"admin@example.com\",\"OutputFormat\":\"Pdf\"}";
                reportEmailJob.IsEnabled = true;
            }
        }
        void SeedCrmData()
        {
            if (ObjectSpace.FirstOrDefault<Organization>(o => o.Name == "Contoso Ltd") != null)
                return;

            // Organizations
            var contoso = ObjectSpace.CreateObject<Organization>();
            contoso.Name = "Contoso Ltd";
            contoso.Address = "123 Innovation Drive";
            contoso.City = "Seattle";
            contoso.Country = "USA";
            contoso.Phone = "+1 206 555 0100";
            contoso.Email = "info@contoso.com";
            contoso.Website = "https://contoso.com";

            var northwind = ObjectSpace.CreateObject<Organization>();
            northwind.Name = "Northwind Traders";
            northwind.Address = "456 Commerce Street";
            northwind.City = "Portland";
            northwind.Country = "USA";
            northwind.Phone = "+1 503 555 0200";
            northwind.Email = "hello@northwind.com";
            northwind.Website = "https://northwind.com";

            var fabrikam = ObjectSpace.CreateObject<Organization>();
            fabrikam.Name = "Fabrikam Inc";
            fabrikam.Address = "78 Tech Park";
            fabrikam.City = "Amsterdam";
            fabrikam.Country = "Netherlands";
            fabrikam.Phone = "+31 20 555 0300";
            fabrikam.Email = "contact@fabrikam.nl";
            fabrikam.Website = "https://fabrikam.nl";

            // Contacts
            var alice = ObjectSpace.CreateObject<Contact>();
            alice.FirstName = "Alice";
            alice.LastName = "Johnson";
            alice.Email = "alice.johnson@contoso.com";
            alice.Phone = "+1 206 555 0101";
            alice.JobTitle = "Project Manager";
            alice.Organization = contoso;

            var bob = ObjectSpace.CreateObject<Contact>();
            bob.FirstName = "Bob";
            bob.LastName = "Smith";
            bob.Email = "bob.smith@contoso.com";
            bob.Phone = "+1 206 555 0102";
            bob.JobTitle = "Software Engineer";
            bob.Organization = contoso;

            var carol = ObjectSpace.CreateObject<Contact>();
            carol.FirstName = "Carol";
            carol.LastName = "Williams";
            carol.Email = "carol.williams@northwind.com";
            carol.Phone = "+1 503 555 0201";
            carol.JobTitle = "Operations Director";
            carol.Organization = northwind;

            var dave = ObjectSpace.CreateObject<Contact>();
            dave.FirstName = "Dave";
            dave.LastName = "Brown";
            dave.Email = "dave.brown@northwind.com";
            dave.Phone = "+1 503 555 0202";
            dave.JobTitle = "Data Analyst";
            dave.Organization = northwind;

            var eva = ObjectSpace.CreateObject<Contact>();
            eva.FirstName = "Eva";
            eva.LastName = "de Vries";
            eva.Email = "eva.devries@fabrikam.nl";
            eva.Phone = "+31 20 555 0301";
            eva.JobTitle = "CTO";
            eva.Organization = fabrikam;

            // Projects
            var webRedesign = ObjectSpace.CreateObject<Project>();
            webRedesign.Name = "Website Redesign";
            webRedesign.Description = "Complete overhaul of the corporate website with new branding and mobile-first design.";
            webRedesign.Status = ProjectStatus.InProgress;
            webRedesign.StartDate = new DateTime(2026, 1, 15);
            webRedesign.DueDate = new DateTime(2026, 6, 30);
            webRedesign.Organization = contoso;

            var dataWarehouse = ObjectSpace.CreateObject<Project>();
            dataWarehouse.Name = "Data Warehouse Migration";
            dataWarehouse.Description = "Migrate legacy data warehouse to cloud-based solution with real-time analytics.";
            dataWarehouse.Status = ProjectStatus.NotStarted;
            dataWarehouse.StartDate = new DateTime(2026, 3, 1);
            dataWarehouse.DueDate = new DateTime(2026, 9, 30);
            dataWarehouse.Organization = northwind;

            var mobileApp = ObjectSpace.CreateObject<Project>();
            mobileApp.Name = "Mobile App v2";
            mobileApp.Description = "Second major version of the customer-facing mobile application.";
            mobileApp.Status = ProjectStatus.InProgress;
            mobileApp.StartDate = new DateTime(2025, 11, 1);
            mobileApp.DueDate = new DateTime(2026, 4, 15);
            mobileApp.Organization = fabrikam;

            var compliance = ObjectSpace.CreateObject<Project>();
            compliance.Name = "GDPR Compliance Audit";
            compliance.Description = "Annual GDPR compliance review and remediation of identified gaps.";
            compliance.Status = ProjectStatus.Completed;
            compliance.StartDate = new DateTime(2025, 9, 1);
            compliance.DueDate = new DateTime(2025, 12, 31);
            compliance.Organization = contoso;

            // Tasks — Website Redesign
            var t1 = ObjectSpace.CreateObject<ProjectTask>();
            t1.Title = "Design wireframes";
            t1.Description = "Create wireframes for all key pages including home, about, and product pages.";
            t1.Status = BusinessObjects.TaskStatus.Done;
            t1.Priority = TaskPriority.High;
            t1.DueDate = new DateTime(2026, 2, 15);
            t1.Project = webRedesign;
            t1.AssignedTo = alice;

            var t2 = ObjectSpace.CreateObject<ProjectTask>();
            t2.Title = "Implement responsive layout";
            t2.Description = "Build the responsive CSS framework and base page templates.";
            t2.Status = BusinessObjects.TaskStatus.InProgress;
            t2.Priority = TaskPriority.High;
            t2.DueDate = new DateTime(2026, 3, 31);
            t2.Project = webRedesign;
            t2.AssignedTo = bob;

            var t3 = ObjectSpace.CreateObject<ProjectTask>();
            t3.Title = "Content migration";
            t3.Description = "Migrate existing content to the new CMS and update formatting.";
            t3.Status = BusinessObjects.TaskStatus.ToDo;
            t3.Priority = TaskPriority.Normal;
            t3.DueDate = new DateTime(2026, 5, 15);
            t3.Project = webRedesign;
            t3.AssignedTo = alice;

            // Tasks — Data Warehouse Migration
            var t4 = ObjectSpace.CreateObject<ProjectTask>();
            t4.Title = "Assess current schema";
            t4.Description = "Document all existing tables, views, and stored procedures.";
            t4.Status = BusinessObjects.TaskStatus.ToDo;
            t4.Priority = TaskPriority.High;
            t4.DueDate = new DateTime(2026, 3, 15);
            t4.Project = dataWarehouse;
            t4.AssignedTo = dave;

            var t5 = ObjectSpace.CreateObject<ProjectTask>();
            t5.Title = "Select cloud provider";
            t5.Description = "Evaluate Azure Synapse, AWS Redshift, and Google BigQuery.";
            t5.Status = BusinessObjects.TaskStatus.ToDo;
            t5.Priority = TaskPriority.Critical;
            t5.DueDate = new DateTime(2026, 3, 31);
            t5.Project = dataWarehouse;
            t5.AssignedTo = carol;

            var t6 = ObjectSpace.CreateObject<ProjectTask>();
            t6.Title = "Build ETL pipelines";
            t6.Description = "Create extract-transform-load pipelines for all data sources.";
            t6.Status = BusinessObjects.TaskStatus.ToDo;
            t6.Priority = TaskPriority.Normal;
            t6.DueDate = new DateTime(2026, 6, 30);
            t6.Project = dataWarehouse;
            t6.AssignedTo = dave;

            // Tasks — Mobile App v2
            var t7 = ObjectSpace.CreateObject<ProjectTask>();
            t7.Title = "API gateway setup";
            t7.Description = "Configure API gateway with rate limiting and authentication.";
            t7.Status = BusinessObjects.TaskStatus.Done;
            t7.Priority = TaskPriority.Critical;
            t7.DueDate = new DateTime(2026, 1, 15);
            t7.Project = mobileApp;
            t7.AssignedTo = eva;

            var t8 = ObjectSpace.CreateObject<ProjectTask>();
            t8.Title = "Push notification service";
            t8.Description = "Implement push notifications for iOS and Android.";
            t8.Status = BusinessObjects.TaskStatus.InProgress;
            t8.Priority = TaskPriority.High;
            t8.DueDate = new DateTime(2026, 3, 15);
            t8.Project = mobileApp;
            t8.AssignedTo = eva;

            var t9 = ObjectSpace.CreateObject<ProjectTask>();
            t9.Title = "Beta testing";
            t9.Description = "Coordinate beta testing with 50 pilot users.";
            t9.Status = BusinessObjects.TaskStatus.ToDo;
            t9.Priority = TaskPriority.Normal;
            t9.DueDate = new DateTime(2026, 4, 1);
            t9.Project = mobileApp;

            // Tasks — GDPR Compliance
            var t10 = ObjectSpace.CreateObject<ProjectTask>();
            t10.Title = "Data inventory";
            t10.Description = "Complete inventory of all personal data processing activities.";
            t10.Status = BusinessObjects.TaskStatus.Done;
            t10.Priority = TaskPriority.Critical;
            t10.DueDate = new DateTime(2025, 10, 15);
            t10.Project = compliance;
            t10.AssignedTo = alice;
        }

        void SeedEmailTemplates()
        {
            if (ObjectSpace.FirstOrDefault<EmailTemplate>(t => t.Name == "Welcome Contact") != null)
                return;

            var welcome = ObjectSpace.CreateObject<EmailTemplate>();
            welcome.Name = "Welcome Contact";
            welcome.Subject = "Welcome, {FirstName}!";
            welcome.BodyHtml = @"<h2>Hello {FullName},</h2>
<p>Welcome to our platform! We're excited to have you on board at <strong>{Organization.Name}</strong>.</p>
<p>As a {JobTitle}, you'll find our tools helpful for managing your projects and tasks.</p>
<p>Best regards,<br/>The XAF Hangfire Team</p>";
            welcome.Description = "Sent to new contacts when they are added to the system.";

            var statusUpdate = ObjectSpace.CreateObject<EmailTemplate>();
            statusUpdate.Name = "Project Status Update";
            statusUpdate.Subject = "Project Status Update for {Organization.Name}";
            statusUpdate.BodyHtml = @"<h2>Hi {FirstName},</h2>
<p>Here is your periodic project status update for <strong>{Organization.Name}</strong>.</p>
<p>Please review the latest project activities and reach out if you have any questions.</p>
<p>Best regards,<br/>The XAF Hangfire Team</p>";
            statusUpdate.Description = "Periodic status update sent to contacts about their organization's projects.";
        }

        PermissionPolicyRole CreateBackgroundJobsRole()
        {
            PermissionPolicyRole role = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == "BackgroundJobs");
            if (role == null)
            {
                role = ObjectSpace.CreateObject<PermissionPolicyRole>();
                role.Name = "BackgroundJobs";

                // Read-only access to types needed by report generation and data queries
                role.AddTypePermissionsRecursively<ReportDataV2>(SecurityOperations.ReadOnlyAccess, SecurityPermissionState.Allow);
                role.AddTypePermissionsRecursively<Organization>(SecurityOperations.ReadOnlyAccess, SecurityPermissionState.Allow);
                role.AddTypePermissionsRecursively<Contact>(SecurityOperations.ReadOnlyAccess, SecurityPermissionState.Allow);
                role.AddTypePermissionsRecursively<Project>(SecurityOperations.ReadOnlyAccess, SecurityPermissionState.Allow);
                role.AddTypePermissionsRecursively<ProjectTask>(SecurityOperations.ReadOnlyAccess, SecurityPermissionState.Allow);
                role.AddTypePermissionsRecursively<JobDefinition>(SecurityOperations.ReadOnlyAccess, SecurityPermissionState.Allow);
                role.AddTypePermissionsRecursively<EmailTemplate>(SecurityOperations.ReadOnlyAccess, SecurityPermissionState.Allow);
                role.AddTypePermissionsRecursively<JobExecutionRecord>(SecurityOperations.ReadOnlyAccess, SecurityPermissionState.Allow);
            }
            return role;
        }

        PermissionPolicyRole CreateDefaultRole()
        {
            PermissionPolicyRole defaultRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(role => role.Name == "Default");
            if (defaultRole == null)
            {
                defaultRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
                defaultRole.Name = "Default";

                defaultRole.AddObjectPermissionFromLambda<ApplicationUser>(SecurityOperations.Read, cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
                defaultRole.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/MyDetails", SecurityPermissionState.Allow);
                defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "ChangePasswordOnFirstLogon", cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
                defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "StoredPassword", cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
                defaultRole.AddTypePermissionsRecursively<PermissionPolicyRole>(SecurityOperations.Read, SecurityPermissionState.Deny);
                defaultRole.AddObjectPermission<ModelDifference>(SecurityOperations.ReadWriteAccess, "UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
                defaultRole.AddObjectPermission<ModelDifferenceAspect>(SecurityOperations.ReadWriteAccess, "Owner.UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
                defaultRole.AddTypePermissionsRecursively<ModelDifference>(SecurityOperations.Create, SecurityPermissionState.Allow);
                defaultRole.AddTypePermissionsRecursively<ModelDifferenceAspect>(SecurityOperations.Create, SecurityPermissionState.Allow);
            }
            return defaultRole;
        }
    }
}
