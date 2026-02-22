using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace xafhangfire.Module.BusinessObjects
{
    public enum TaskStatus
    {
        [Description("To Do")]
        ToDo = 0,
        [Description("In Progress")]
        InProgress = 1,
        Done = 2,
        Blocked = 3
    }

    public enum TaskPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    [DefaultClassOptions]
    [NavigationItem("CRM")]
    [DefaultProperty(nameof(Title))]
    public class ProjectTask
    {
        [Key]
        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public virtual string Title { get; set; } = string.Empty;

        [FieldSize(FieldSizeAttribute.Unlimited)]
        public virtual string Description { get; set; }

        public virtual TaskStatus Status { get; set; } = TaskStatus.ToDo;

        public virtual TaskPriority Priority { get; set; } = TaskPriority.Normal;

        public virtual DateTime? DueDate { get; set; }

        public virtual Project Project { get; set; }

        public virtual Contact AssignedTo { get; set; }
    }
}
