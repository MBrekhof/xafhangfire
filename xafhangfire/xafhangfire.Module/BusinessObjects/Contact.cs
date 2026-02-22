using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace xafhangfire.Module.BusinessObjects
{
    [DefaultClassOptions]
    [NavigationItem("CRM")]
    [DefaultProperty(nameof(FullName))]
    public class Contact
    {
        [Key]
        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public virtual string FirstName { get; set; } = string.Empty;

        [Required]
        public virtual string LastName { get; set; } = string.Empty;

        [VisibleInDetailView(false)]
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string FullName => $"{FirstName} {LastName}";

        public virtual string Email { get; set; }

        public virtual string Phone { get; set; }

        public virtual string JobTitle { get; set; }

        public virtual Organization Organization { get; set; }

        [FieldSize(FieldSizeAttribute.Unlimited)]
        public virtual string Notes { get; set; }
    }
}
