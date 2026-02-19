using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace xafhangfire.Module.BusinessObjects
{
    [DefaultClassOptions]
    [NavigationItem("CRM")]
    [DefaultProperty(nameof(Name))]
    [DomainComponent]
    public class Organization
    {
        [Key]
        [VisibleInDetailView(false), VisibleInListView(false)]
        public virtual Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public virtual string Name { get; set; } = string.Empty;

        public virtual string Address { get; set; }

        public virtual string City { get; set; }

        public virtual string Country { get; set; }

        public virtual string Phone { get; set; }

        public virtual string Email { get; set; }

        public virtual string Website { get; set; }

        [FieldSize(FieldSizeAttribute.Unlimited)]
        public virtual string Notes { get; set; }

        public virtual IList<Contact> Contacts { get; set; } = new List<Contact>();

        public virtual IList<Project> Projects { get; set; } = new List<Project>();
    }
}
