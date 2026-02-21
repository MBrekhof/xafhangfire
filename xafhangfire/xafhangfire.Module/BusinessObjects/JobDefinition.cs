using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using xafhangfire.Module.Helpers;

namespace xafhangfire.Module.BusinessObjects
{
    public enum JobRunStatus
    {
        [Description("Never Run")]
        NeverRun = 0,
        Running = 1,
        Success = 2,
        Failed = 3
    }

    [DefaultClassOptions]
    [NavigationItem("Jobs")]
    [DefaultProperty(nameof(Name))]
    [DomainComponent]
    public class JobDefinition
    {
        [Key]
        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public virtual string Name { get; set; } = string.Empty;

        [Required]
        public virtual string JobTypeName { get; set; } = string.Empty;

        [FieldSize(FieldSizeAttribute.Unlimited)]
        [EditorAlias("JobParametersEditor")]
        public virtual string ParametersJson { get; set; }

        public virtual string CronExpression { get; set; }

        [NotMapped]
        [VisibleInListView(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string CronDescription => CronHelper.GetDescription(CronExpression);

        [NotMapped]
        [FieldSize(FieldSizeAttribute.Unlimited)]
        [VisibleInListView(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string NextScheduledRuns => CronHelper.FormatNextRuns(CronExpression, 5);

        public virtual bool IsEnabled { get; set; } = true;

        [VisibleInDetailView(true), VisibleInListView(true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual DateTime? LastRunUtc { get; set; }

        [VisibleInDetailView(true), VisibleInListView(true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual DateTime? NextRunUtc { get; set; }

        [VisibleInDetailView(true), VisibleInListView(true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual JobRunStatus LastRunStatus { get; set; } = JobRunStatus.NeverRun;

        [FieldSize(FieldSizeAttribute.Unlimited)]
        [VisibleInListView(false)]
        public virtual string LastRunMessage { get; set; }

        public virtual int ConsecutiveFailures { get; set; }

        public virtual ObservableCollection<JobExecutionRecord> ExecutionHistory { get; set; } = new();
    }
}
