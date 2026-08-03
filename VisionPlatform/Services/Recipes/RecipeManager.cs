using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using VisionPlatform.Models;

namespace VisionPlatform.Services.Recipes;

/// <summary>配方管理器：加载/保存/增删配方（JSON），持有当前生效配方。</summary>
public sealed class RecipeManager
{
    private readonly string _dir;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public RecipeManager(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        LoadAll();
        CurrentRecipe = Recipes.FirstOrDefault() ?? new Recipe();
        CurrentRecipe.IsCurrent = true;
    }

    public ObservableCollection<Recipe> Recipes { get; } = [];

    public Recipe CurrentRecipe { get; private set; }

    public event Action<Recipe>? CurrentRecipeChanged;

    private void LoadAll()
    {
        foreach (var file in Directory.GetFiles(_dir, "*.json"))
        {
            try
            {
                var recipe = JsonSerializer.Deserialize<Recipe>(File.ReadAllText(file));
                if (recipe is null) continue;
                recipe.FilePath = file;
                Recipes.Add(recipe);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"配方加载失败: {file} - {ex.Message}");
            }
        }
    }

    public void Apply(Recipe recipe)
    {
        CurrentRecipe.IsCurrent = false;
        CurrentRecipe = recipe;
        recipe.IsCurrent = true;
        CurrentRecipeChanged?.Invoke(recipe);
    }

    public void Add(Recipe recipe)
    {
        recipe.FilePath = GetUniquePath(recipe.Name);
        Save(recipe);
        Recipes.Add(recipe);
    }

    public void Save(Recipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.FilePath))
            recipe.FilePath = GetUniquePath(recipe.Name);
        recipe.Name = recipe.Name.Trim();
        if (string.IsNullOrEmpty(recipe.Name)) recipe.Name = "未命名";
        var json = JsonSerializer.Serialize(recipe, _json);
        File.WriteAllText(recipe.FilePath, json);
    }

    public void Delete(Recipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.FilePath)) return;
        try { File.Delete(recipe.FilePath); } catch { }
        Recipes.Remove(recipe);
        if (ReferenceEquals(CurrentRecipe, recipe) || CurrentRecipe.Name == recipe.Name)
        {
            Apply(Recipes.FirstOrDefault() ?? new Recipe());
        }
    }

    private string GetUniquePath(string name)
    {
        var baseName = string.Concat(name.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrEmpty(baseName)) baseName = "配方";
        var path = Path.Combine(_dir, $"{baseName}.json");
        var i = 1;
        while (File.Exists(path))
            path = Path.Combine(_dir, $"{baseName}_{i++}.json");
        return path;
    }
}
