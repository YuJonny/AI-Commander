using System.Windows.Controls;

namespace Commander
{
    public partial class AboutPage : UserControl
    {
        public AboutPage()
        {
            InitializeComponent();
            RefreshLocalization();
        }

        public void RefreshLocalization()
        {
            LblAppName.Text = I18n.Get("title.app");
            LblDesc.Text = I18n.Get("about.description");
            LblRecHeader.Text = I18n.Get("about.recommended").ToUpper();
            LnkTypeless.Content = I18n.Get("about.app.typeless");
            LblTypelessDesc.Text = I18n.Get("about.app.typeless.desc");
            LblHwHeader.Text = I18n.Get("about.recommendedHardware").ToUpper();
            LnkNativeUnion.Content = I18n.Get("about.hw.nativeunion");
            LblNativeUnionDesc.Text = I18n.Get("about.hw.nativeunion.desc");
        }
    }
}
