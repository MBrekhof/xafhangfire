using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace xafhangfire.Module.BusinessObjects
{
    [DefaultClassOptions]
    [NavigationItem("Jobs")]
    [DefaultProperty(nameof(JobName))]
    [DomainComponent]
    public class JobExecutionRecord
    {
        [Key]
        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual Guid Id { get; set; } = Guid.NewGuid();

        public virtual string JobName { get; set; } = string.Empty;

        public virtual string JobTypeName { get; set; } = string.Empty;

        public virtual DateTime StartedUtc { get; set; }

        public virtual DateTime? CompletedUtc { get; set; }

        public virtual JobRunStatus Status { get; set; } = JobRunStatus.Running;

        [FieldSize(FieldSizeAttribute.Unlimited)]
        [VisibleInListView(false)]
        public virtual string ErrorMessage { get; set; }

        public virtual long DurationMs { get; set; }

        public virtual int ProgressPercent { get; set; }

        public virtual string ProgressMessage { get; set; }

        [FieldSize(FieldSizeAttribute.Unlimited)]
        [VisibleInListView(false)]
        public virtual string ParametersJson { get; set; }

        public virtual JobDefinition JobDefinition { get; set; }
    }
}
