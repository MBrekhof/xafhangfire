using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;

namespace xafhangfire.Module.BusinessObjects
{
    [DefaultClassOptions]
    [NavigationItem("Jobs")]
    [DefaultProperty(nameof(JobName))]
    public class JobExecutionRecord
    {
        [Key]
        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual Guid Id { get; set; } = Guid.NewGuid();

        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual string JobName { get; set; } = string.Empty;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual string JobTypeName { get; set; } = string.Empty;

        [ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual DateTime StartedUtc { get; set; }

        [ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual DateTime? CompletedUtc { get; set; }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual JobRunStatus Status { get; set; } = JobRunStatus.Running;

        [FieldSize(FieldSizeAttribute.Unlimited)]
        [VisibleInListView(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual string ErrorMessage { get; set; }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual long DurationMs { get; set; }

        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual int ProgressPercent { get; set; }

        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual string ProgressMessage { get; set; }

        [FieldSize(FieldSizeAttribute.Unlimited)]
        [VisibleInListView(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual string ParametersJson { get; set; }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual JobDefinition JobDefinition { get; set; }
    }
}
