using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionPlatform.Infrastructure;
using VisionPlatform.Models;

namespace VisionPlatform.ViewModels;

/// <summary>配方管理页面 VM。</summary>
public partial class RecipeViewModel : ObservableObject
{
    public ObservableCollection<Recipe> Recipes => ServiceLocator.Recipes.Recipes;

    [ObservableProperty]
    private Recipe? _selectedRecipe;

    [ObservableProperty]
    private Recipe _currentRecipe;

    [ObservableProperty]
    private bool _isEditing = true;

    public RecipeViewModel()
    {
        CurrentRecipe = ServiceLocator.Recipes.CurrentRecipe;
        SelectedRecipe = CurrentRecipe;
    }

    partial void OnSelectedRecipeChanged(Recipe? value)
    {
        if (value is not null) CurrentRecipe = value;
    }

    [RelayCommand]
    private void ApplyRecipe()
    {
        if (SelectedRecipe is null) return;
        ServiceLocator.Recipes.Apply(SelectedRecipe);
        CurrentRecipe = SelectedRecipe;
        ServiceLocator.Log.Info($"已应用配方: {SelectedRecipe.Name}");
    }

    [RelayCommand]
    private void NewRecipe()
    {
        var recipe = new Recipe
        {
            Name = $"新配方_{DateTime.Now:MMddHHmmss}"
        };
        ServiceLocator.Recipes.Add(recipe);
        SelectedRecipe = recipe;
        CurrentRecipe = recipe;
        ServiceLocator.Log.Info($"已新建配方: {recipe.Name}");
    }

    [RelayCommand]
    private void SaveRecipe()
    {
        if (SelectedRecipe is null) return;
        ServiceLocator.Recipes.Save(SelectedRecipe);
        OnPropertyChanged(nameof(Recipes));
        ServiceLocator.Log.Info($"配方已保存: {SelectedRecipe.Name}");
    }

    [RelayCommand]
    private void DeleteRecipe()
    {
        if (SelectedRecipe is null) return;
        var name = SelectedRecipe.Name;
        ServiceLocator.Recipes.Delete(SelectedRecipe);
        SelectedRecipe = Recipes.FirstOrDefault();
        CurrentRecipe = ServiceLocator.Recipes.CurrentRecipe;
        ServiceLocator.Log.Info($"已删除配方: {name}");
    }

    [RelayCommand]
    private void CloneRecipe()
    {
        if (SelectedRecipe is null) return;
        var clone = SelectedRecipe.Clone();
        clone.Name = $"{SelectedRecipe.Name}_副本";
        clone.FilePath = "";
        ServiceLocator.Recipes.Add(clone);
        SelectedRecipe = clone;
        ServiceLocator.Log.Info($"已复制配方: {clone.Name}");
    }
}
