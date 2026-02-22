using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Xpo;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Module.Reports
{
    [DomainComponent]
    public class ProjectStatusReportParameters : ReportParametersObjectBase
    {
        public DateTime StartDate { get; set; } = new DateTime(2000, 1, 1);
        public DateTime EndDate { get; set; } = DateTime.Now.AddYears(1);

        public ProjectStatusReportParameters(IObjectSpaceCreator provider) : base(provider) { }

        protected override IObjectSpace CreateObjectSpace()
            => objectSpaceCreator.CreateObjectSpace(typeof(Project));

        public override CriteriaOperator GetCriteria()
            => CriteriaOperator.Parse("[StartDate] >= ? And [StartDate] <= ?", StartDate, EndDate);

        public override SortProperty[] GetSorting() => null;
    }
}
