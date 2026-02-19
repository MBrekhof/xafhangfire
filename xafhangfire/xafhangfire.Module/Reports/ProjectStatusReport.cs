using System.Drawing;
using DevExpress.Drawing;
using DevExpress.Persistent.Base.ReportsV2;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;

namespace xafhangfire.Module.Reports
{
    public class ProjectStatusReport : XtraReport
    {
        public ProjectStatusReport()
        {
            InitializeReport();
        }

        private void InitializeReport()
        {
            var dataSource = new CollectionDataSource();
            dataSource.ObjectTypeName = "xafhangfire.Module.BusinessObjects.Project";
            DataSource = dataSource;

            Landscape = true;

            var reportHeader = new ReportHeaderBand { HeightF = 60 };
            Bands.Add(reportHeader);

            var titleLabel = new XRLabel
            {
                Text = "Project Status Report",
                SizeF = new SizeF(700, 30),
                LocationF = new PointF(0, 5),
                Font = new DXFont("Arial", 18, DXFontStyle.Bold)
            };
            reportHeader.Controls.Add(titleLabel);

            var dateLabel = new XRLabel
            {
                SizeF = new SizeF(700, 20),
                LocationF = new PointF(0, 35),
                Font = new DXFont("Arial", 9, DXFontStyle.Italic)
            };
            dateLabel.ExpressionBindings.Add(
                new ExpressionBinding("BeforePrint", "Text", "'Generated: ' + Now()"));
            reportHeader.Controls.Add(dateLabel);

            float[] widths = { 180, 140, 100, 100, 100, 180 };
            string[] headers = { "Project", "Organization", "Status", "Start Date", "Due Date", "Description" };
            string[] fields = { "[Name]", "[Organization.Name]", "[Status]", "[StartDate]", "[DueDate]", "[Description]" };

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
                    BorderColor = Color.FromArgb(220, 220, 220),
                    CanGrow = true
                };
                label.ExpressionBindings.Add(
                    new ExpressionBinding("BeforePrint", "Text", fields[i]));
                detail.Controls.Add(label);
                x += widths[i];
            }

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
