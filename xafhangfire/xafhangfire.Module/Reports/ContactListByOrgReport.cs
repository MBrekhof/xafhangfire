using System.Drawing;
using DevExpress.Drawing;
using DevExpress.Persistent.Base.ReportsV2;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;

namespace xafhangfire.Module.Reports
{
    public class ContactListByOrgReport : XtraReport
    {
        public ContactListByOrgReport()
        {
            InitializeReport();
        }

        private void InitializeReport()
        {
            var dataSource = new CollectionDataSource();
            dataSource.ObjectTypeName = "xafhangfire.Module.BusinessObjects.Contact";
            dataSource.Sorting.Add(new DevExpress.Xpo.SortProperty("Organization.Name", DevExpress.Xpo.DB.SortingDirection.Ascending));
            DataSource = dataSource;

            var reportHeader = new ReportHeaderBand { HeightF = 60 };
            Bands.Add(reportHeader);

            var titleLabel = new XRLabel
            {
                Text = "Contact List by Organization",
                SizeF = new SizeF(650, 30),
                LocationF = new PointF(0, 5),
                Font = new DXFont("Arial", 18, DXFontStyle.Bold)
            };
            reportHeader.Controls.Add(titleLabel);

            var dateLabel = new XRLabel
            {
                SizeF = new SizeF(650, 20),
                LocationF = new PointF(0, 35),
                Font = new DXFont("Arial", 9, DXFontStyle.Italic)
            };
            dateLabel.ExpressionBindings.Add(
                new ExpressionBinding("BeforePrint", "Text", "'Generated: ' + Now()"));
            reportHeader.Controls.Add(dateLabel);

            // Group by Organization
            var groupHeader = new GroupHeaderBand { HeightF = 30 };
            groupHeader.GroupFields.Add(new GroupField("Organization.Name"));
            Bands.Add(groupHeader);

            var orgLabel = new XRLabel
            {
                SizeF = new SizeF(650, 30),
                LocationF = new PointF(0, 0),
                Font = new DXFont("Arial", 12, DXFontStyle.Bold),
                BackColor = Color.FromArgb(236, 240, 241),
                Padding = new PaddingInfo(10, 5, 5, 5),
                Borders = BorderSide.Bottom,
                BorderColor = Color.FromArgb(41, 128, 185),
                BorderWidth = 2
            };
            orgLabel.ExpressionBindings.Add(
                new ExpressionBinding("BeforePrint", "Text", "[Organization.Name]"));
            groupHeader.Controls.Add(orgLabel);

            // Column headers
            float[] widths = { 120, 120, 180, 120, 120 };
            string[] headers = { "First Name", "Last Name", "Email", "Phone", "Job Title" };
            string[] fields = { "[FirstName]", "[LastName]", "[Email]", "[Phone]", "[JobTitle]" };

            var pageHeader = new PageHeaderBand { HeightF = 25 };
            Bands.Add(pageHeader);

            float x = 0;
            for (int i = 0; i < headers.Length; i++)
            {
                var header = new XRLabel
                {
                    Text = headers[i],
                    SizeF = new SizeF(widths[i], 25),
                    LocationF = new PointF(x, 0),
                    Font = new DXFont("Arial", 9, DXFontStyle.Bold),
                    BackColor = Color.FromArgb(41, 128, 185),
                    ForeColor = Color.White,
                    Padding = new PaddingInfo(5, 5, 3, 3),
                    Borders = BorderSide.All,
                    BorderColor = Color.FromArgb(200, 200, 200)
                };
                pageHeader.Controls.Add(header);
                x += widths[i];
            }

            // Detail band
            var detail = new DetailBand { HeightF = 25 };
            Bands.Add(detail);

            x = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                var label = new XRLabel
                {
                    SizeF = new SizeF(widths[i], 25),
                    LocationF = new PointF(x, 0),
                    Font = new DXFont("Arial", 8),
                    Padding = new PaddingInfo(5, 5, 3, 3),
                    Borders = BorderSide.All,
                    BorderColor = Color.FromArgb(220, 220, 220)
                };
                label.ExpressionBindings.Add(
                    new ExpressionBinding("BeforePrint", "Text", fields[i]));
                detail.Controls.Add(label);
                x += widths[i];
            }

            // Group footer with count
            var groupFooter = new GroupFooterBand { HeightF = 25 };
            Bands.Add(groupFooter);

            var countLabel = new XRLabel
            {
                SizeF = new SizeF(650, 25),
                LocationF = new PointF(0, 0),
                Font = new DXFont("Arial", 8, DXFontStyle.Italic),
                TextAlignment = TextAlignment.TopRight,
                Padding = new PaddingInfo(5, 10, 3, 3)
            };
            countLabel.Summary = new XRSummary
            {
                Running = SummaryRunning.Group
            };
            countLabel.ExpressionBindings.Add(
                new ExpressionBinding("BeforePrint", "Text",
                    "'Contacts: ' + sumRecordCount()"));
            groupFooter.Controls.Add(countLabel);

            // Page footer
            var pageFooter = new PageFooterBand { HeightF = 25 };
            Bands.Add(pageFooter);

            var pageInfo = new XRPageInfo
            {
                SizeF = new SizeF(200, 20),
                LocationF = new PointF(0, 2),
                Font = new DXFont("Arial", 8),
                PageInfo = PageInfo.NumberOfTotal
            };
            pageFooter.Controls.Add(pageInfo);
        }
    }
}
