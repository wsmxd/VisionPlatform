using System.Windows.Controls;
using VisionPlatform.Infrastructure;

namespace VisionPlatform.Views;

public partial class RecipeView : UserControl
{
    public RecipeView()
    {
        InitializeComponent();
        DataContext = ServiceLocator.Recipe;
    }
}
