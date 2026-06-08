using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;

namespace Commander
{
    public partial class LanguagePage : UserControl
    {
        public LanguagePage()
        {
            InitializeComponent();
            RefreshLocalization();
            UpdateButtons();
        }

        public void RefreshLocalization()
        {
            LblTitle.Text = I18n.Get("lang.title");
            LblDesc.Text = I18n.Get("lang.description");
        }

        private void UpdateButtons()
        {
            bool isCh = I18n.IsChinese;
            BtnZh.IsChecked = isCh;
            BtnEn.IsChecked = !isCh;
        }

        private void BtnZh_Click(object sender, RoutedEventArgs e)
        {
            I18n.CurrentLanguage = "zh-Hans";
            UpdateButtons();
        }

        private void BtnEn_Click(object sender, RoutedEventArgs e)
        {
            I18n.CurrentLanguage = "en";
            UpdateButtons();
        }
    }
}
