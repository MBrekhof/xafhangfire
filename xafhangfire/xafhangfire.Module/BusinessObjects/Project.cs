using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace xafhangfire.Module.BusinessObjects
{
    public enum ProjectStatus
    {
        [Description("Not Started")]
        NotStarted = 0,
        [Description("In Progress")]
        InProgress = 1,
        [Description("On Hold")]
        OnHold = 2,
        Completed = 3,
        Cancelled = 4
    }

    [DefaultClassOptions]
    [NavigationItem("CRM")]
    [DefaultProperty(nameof(Name))]
    [DomainComponent]
    public class Project
    {
        [Key]
        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public virtual string Name { get; set; } = string.Empty;

        [FieldSize(FieldSizeAttribute.Unlimited)]
        public virtual string Description { get; set; }

        public virtual ProjectStatus Status { get; set; } = ProjectStatus.NotStarted;

        public virtual DateTime? StartDate { get; set; }

        public virtual DateTime? DueDate { get; set; }

        public virtual Organization Organization { get; set; }

        public virtual IList<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
    }
}
