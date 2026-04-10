using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ArchitectFlow_AI
{
    /// <summary>
    /// Interaction logic for ArchitectWindowControl.
    /// </summary>
    public partial class ArchitectWindowControl : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchitectWindowControl"/> class.
        /// </summary>
        public ArchitectWindowControl()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Handles click on the button by displaying a message box.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        [SuppressMessage("Microsoft.Globalization", "CA1300:SpecifyMessageBoxOptions", Justification = "Sample code")]
        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "Default event handler naming pattern")]
        private void button1_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                string.Format(System.Globalization.CultureInfo.CurrentUICulture, "Invoked '{0}'", this.ToString()),
                "ArchitectWindow");
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            var selectedProjects = LstProjects.SelectedItems.Cast<ProjectContext>().ToList();

            // In un caso reale, qui useresti HttpClient per scaricare dai link
            string fakeAC = "AC1: Saldo non negativo. AC2: Max prelievo 500€.";
            string fakeWiki = "SELECT * FROM Account WHERE Id = @Id";

            var builder = new PromptBuilder();
            string finalPrompt = builder.Build(fakeAC, fakeWiki, selectedProjects);

            // Copia il prompt negli appunti per l'uso immediato in Copilot
            System.Windows.Forms.Clipboard.SetText(finalPrompt);

            MessageBox.Show("Prompt Architetturale generato e copiato negli appunti! Incollalo nella chat di Copilot.");
        }
    }
}