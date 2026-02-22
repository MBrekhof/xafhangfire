using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Xpo;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Module.Reports
{
    [DomainComponent]
    public class ContactListByOrgReportParameters : ReportParametersObjectBase
    {
        public Organization Organization { get; set; }

        public ContactListByOrgReportParameters(IObjectSpaceCreator provider) : base(provider) { }

        protected override IObjectSpace CreateObjectSpace()
            => objectSpaceCreator.CreateObjectSpace(typeof(Contact));

        public override CriteriaOperator GetCriteria()
        {
            if (Organization == null)
                return null;
            return CriteriaOperator.Parse("Organization.Id = ?", Organization.Id);
        }

        public override SortProperty[] GetSorting() => null;
    }
}
