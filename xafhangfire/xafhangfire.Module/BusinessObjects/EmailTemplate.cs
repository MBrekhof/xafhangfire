using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace xafhangfire.Module.BusinessObjects
{
    [DefaultClassOptions]
    [NavigationItem("Email")]
    [DefaultProperty(nameof(Name))]
    [DomainComponent]
    public class EmailTemplate
    {
        [Key]
        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public virtual string Name { get; set; } = string.Empty;

        [Required]
        public virtual string Subject { get; set; } = string.Empty;

        [FieldSize(FieldSizeAttribute.Unlimited)]
        public virtual string BodyHtml { get; set; } = string.Empty;

        [FieldSize(500)]
        public virtual string Description { get; set; } = string.Empty;
    }
}
