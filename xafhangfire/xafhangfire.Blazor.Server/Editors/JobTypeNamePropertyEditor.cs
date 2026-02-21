using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Editors;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using Microsoft.AspNetCore.Components;
using xafhangfire.Jobs;

namespace xafhangfire.Blazor.Server.Editors;

[PropertyEditor(typeof(string), "JobTypeNameEditor", false)]
public class JobTypeNamePropertyEditor : BlazorPropertyEditorBase
{
    public JobTypeNamePropertyEditor(Type objectType, IModelMemberViewItem model)
        : base(objectType, model) { }

    public override JobTypeNameComboBoxModel ComponentModel =>
        (JobTypeNameComboBoxModel)base.ComponentModel;

    protected override IComponentModel CreateComponentModel()
    {
        var model = new JobTypeNameComboBoxModel();
        model.Items = CommandMetadataProvider.GetRegisteredTypeNames().ToList();
        model.ValueChanged = EventCallback.Factory.Create<string>(this, value =>
        {
            model.Value = value;
            OnControlValueChanged();
            WriteValue();
        });
        return model;
    }

    protected override void ReadValueCore()
    {
        base.ReadValueCore();
        ComponentModel.Value = (string)PropertyValue ?? string.Empty;
        ComponentModel.ReadOnly = !AllowEdit;
    }

    protected override object GetControlValueCore() => ComponentModel.Value;
}
