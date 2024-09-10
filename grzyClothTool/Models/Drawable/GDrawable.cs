﻿using grzyClothTool.Extensions;
using grzyClothTool.Helpers;
using grzyClothTool.Views;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using CodeWalker.GameFiles;
using System.Linq;
using System;
using grzyClothTool.Controls;
using System.Runtime.Serialization;
using grzyClothTool.Models.Texture;
using System.Security.Cryptography;
using System.Collections.Specialized;

namespace grzyClothTool.Models.Drawable;
#nullable enable

public class GDrawable : INotifyPropertyChanged
{
    private readonly static SemaphoreSlim _semaphore = new(3);
    private readonly static SemaphoreSlim _semaphoreDublicateCheck = new(1);

    public event PropertyChangedEventHandler PropertyChanged;

    public string FilePath { get; set; }

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
        }
    }

    public virtual bool IsReserved => false;

    public int TypeNumeric { get; set; }
    private string _typeName;
    public string TypeName
    {
        get
        {
            _typeName ??= EnumHelper.GetName(TypeNumeric, IsProp);
            return _typeName;
        }
        set
        {
            _typeName = value;

            //TypeNumeric = EnumHelper.GetValue(value, IsProp);

            SetDrawableName();
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public List<string> AvailableTypes => IsProp ? EnumHelper.GetPropTypeList() : EnumHelper.GetDrawableTypeList();

    /// <returns>
    /// true(1) = male ped, false(0) = female ped
    /// </returns>
    public bool Sex { get; set; }
    public bool IsProp { get; set; }
    public bool IsComponent => !IsProp;

    public int Number { get; set; }
    public string DisplayNumber => (Number % GlobalConstants.MAX_DRAWABLES_IN_ADDON).ToString("D3");

    public GDrawableDetails Details { get; set; }


    private bool _hasSkin;
    public bool HasSkin
    {
        get { return _hasSkin; }
        set
        {
            if (_hasSkin != value)
            {
                _hasSkin = value;

                foreach (var txt in Textures)
                {
                    txt.HasSkin = value;
                }
                SetDrawableName();
                OnPropertyChanged();
            }
        }
    }

    private bool _enableKeepPreview;
    public bool EnableKeepPreview
    {
        get => _enableKeepPreview;
        set { _enableKeepPreview = value; OnPropertyChanged(); }
    }

    public float HairScaleValue { get; set; } = 0.5f;


    private bool _enableHairScale;
    public bool EnableHairScale
    {
        get => _enableHairScale;
        set { _enableHairScale = value; OnPropertyChanged(); }
    }

    public float HighHeelsValue { get; set; } = 1.0f;
    private bool _enableHighHeels;
    public bool EnableHighHeels
    {
        get => _enableHighHeels;
        set { _enableHighHeels = value; OnPropertyChanged(); }
    }

    private string _audio;
    public string Audio
    {
        get => _audio;
        set
        {
            _audio = value;
            OnPropertyChanged();
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    [JsonIgnore]
    internal static Tuple<Dictionary<string, List<GDrawable>>, Dictionary<string, List<GDrawable>>> hashes = Tuple.Create(new Dictionary<string, List<GDrawable>>(), new Dictionary<string, List<GDrawable>>());

    [JsonIgnore]
    private bool _isDuplicate = false;

    [JsonIgnore]
    public bool IsDuplicate
    {
        get => SettingsHelper.Instance.DisplayHashDuplicate && _isDuplicate;
        set
        {
            _isDuplicate = value;
            OnPropertyChanged(nameof(IsDuplicate));
        }
    }

    [JsonIgnore]
    private List<GDrawable> _isDuplicateName = [];

    [JsonIgnore]
    public string IsDuplicateName
    {
        get
        {
            if (IsDuplicate == false)
            {
                return "";
            }
            var namedDuplicateList = _isDuplicateName.Select(drawable => {
                Addon? addon = MainWindow.AddonManager.Addons.FirstOrDefault(a => a?.Drawables?.Contains(drawable) == true, null);
                if (addon == null)
                {
                    return "Not found: " + drawable.Name;
                }
                return addon.Name + "/" + drawable.Name;
            });
            return string.Join(", ", namedDuplicateList);
        }
    }

    [JsonIgnore]
    public List<GDrawable> IsDuplicateNameSetter
    {
        set
        {
            _isDuplicateName = value;
            OnPropertyChanged(nameof(_isDuplicateName));
        }
    }

    [JsonIgnore]
    public List<string> AvailableAudioList => EnumHelper.GetAudioList(TypeNumeric);

    private ObservableCollection<SelectableItem> _selectedFlags = [];
    public ObservableCollection<SelectableItem> SelectedFlags
    {
        get => _selectedFlags;
        set
        {
            _selectedFlags = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Flags));
            OnPropertyChanged(nameof(FlagsText));
        }
    }

    [JsonIgnore]
    public string FlagsText
    {
        get
        {
            var count = SelectedFlags.Count(i => i.IsSelected && i.Value != (int)Enums.DrawableFlags.NONE);

            return count > 0 ? $"{Flags} ({count} selected)" : "NONE";
        } 
    }

    [JsonIgnore]
    public int Flags => SelectedFlags.Where(f => f.IsSelected).Sum(f => f.Value);

    [JsonIgnore]
    public List<SelectableItem> AvailableFlags => EnumHelper.GetFlags(Flags);

    public string RenderFlag { get; set; } = ""; // "" is the default value

    [JsonIgnore]
    public static List<string> AvailableRenderFlagList => ["", "PRF_ALPHA", "PRF_DECAL", "PRF_CUTOUT"];

    public ObservableCollection<Texture.GTexture> Textures { get; set; }

    [JsonIgnore]
    public Task DrawableDetailsTask { get; set; } = Task.CompletedTask;

    public GDrawable(string drawablePath, bool isMale, bool isProp, int compType, int count, bool hasSkin, ObservableCollection<GTexture> textures)
    {
        IsLoading = true;

        FilePath = drawablePath;
        Textures = textures;
        TypeNumeric = compType;
        Number = count;
        HasSkin = hasSkin;
        Sex = isMale;
        IsProp = isProp;

        Audio = "none";
        SetDrawableName();

        Load();
    }

    private void Load()
    {
        DrawableDetailsTask = Task.Run(async () => {
            try
            {
                if (FilePath != null)
                {
                    var gDrawableDetails = await LoadDrawableDetailsWithConcurrencyControl(FilePath);
                    if (gDrawableDetails != null)
                    {
                        Details = gDrawableDetails;
                        OnPropertyChanged(nameof(Details));
                        await InitAfterLoading();
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                //todo: add some warning that it couldn't load
            }
            finally
            {
                IsLoading = false;
            }
        });
    }

    private async Task InitAfterLoading()
    {
        Textures.CollectionChanged += async (s, e) =>
        {
            await DrawableDetailsTask;
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0)
            {
                foreach (var newItem in e.NewItems)
                {
                    if (newItem is GTexture texture)
                    {
                        await texture.CheckForDuplicate(Details.Hash, Sex);
                    }
                }
            }
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems?.Count > 0)
            {
                foreach (var oldItem in e.OldItems)
                {
                    if (oldItem is GTexture texture)
                    {
                        await texture.RemoveDuplicate(Details.Hash, Sex);
                    }
                }
            }
        };
        await CheckForDuplicate();
    }


    //this will be called after deserialization
    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {

        SetDrawableName();
        if (IsLoading)
        {
            Load();
        }
        else
        {
            DrawableDetailsTask = Task.CompletedTask;
            _ = InitAfterLoading();
        }
    }

    protected GDrawable(bool isMale, bool isProp, int compType, int count) { /* Used in GReservedDrawable */ }

    public void SetDrawableName()
    {
        string name = $"{TypeName}_{DisplayNumber}";
        var finalName = IsProp ? name : $"{name}_{(HasSkin ? "r" : "u")}";

        Name = finalName;
        //texture number needs to be updated too
        foreach (var txt in Textures)
        {
            txt.Number = Number;
            txt.TypeNumeric = TypeNumeric;
        }
    }

    public void ChangeDrawableType(string newType)
    {
        var newTypeNumeric = EnumHelper.GetValue(newType, IsProp);
        var reserved = new GDrawableReserved(Sex, IsProp, TypeNumeric, Number);
        var index = MainWindow.AddonManager.SelectedAddon.Drawables.IndexOf(this);

        // change current drawable to new type
        Number = MainWindow.AddonManager.SelectedAddon.GetNextDrawableNumber(newTypeNumeric, IsProp, Sex);
        TypeNumeric = newTypeNumeric;
        SetDrawableName();

        // add new drawable with new number and type
        MainWindow.AddonManager.SelectedAddon.Drawables.Insert(index + 1, this);

        // replace drawable with reserved in the same place
        MainWindow.AddonManager.SelectedAddon.Drawables[index] = reserved;

        MainWindow.AddonManager.SelectedAddon.Drawables.Sort();
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static async Task<GDrawableDetails?> LoadDrawableDetailsWithConcurrencyControl(string path)
    {
        await _semaphore.WaitAsync();
        try
        {
            return await GetDrawableDetailsAsync(path);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task<GDrawableDetails?> GetDrawableDetailsAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);

        var yddFile = new YddFile();
        await yddFile.LoadAsync(bytes);

        if (yddFile.DrawableDict.Drawables.Count == 0)
        {
            return null;
        }

        GDrawableDetails details = new();
        details.Hash = string.Concat(MD5.HashData(bytes).Select(x => x.ToString("X2")));


        //is it always 2 and 4?
        var spec = (yddFile.Drawables.First().ShaderGroup.Shaders.data_items.First().ParametersList.Parameters[3].Data as CodeWalker.GameFiles.Texture);
        var normal = (yddFile.Drawables.First().ShaderGroup.Shaders.data_items.First().ParametersList.Parameters[2].Data as CodeWalker.GameFiles.Texture);

        foreach (GDrawableDetails.EmbeddedTextureType txtType in Enum.GetValues(typeof(GDrawableDetails.EmbeddedTextureType)))
        {
            var texture = txtType switch
            {
                GDrawableDetails.EmbeddedTextureType.Specular => spec,
                GDrawableDetails.EmbeddedTextureType.Normal => normal,
                _ => null
            };

            if (texture == null)
            {
                continue;
            }

            details.EmbeddedTextures[txtType] = new GTextureDetails
            {
                Width = texture.Width,
                Height = texture.Height,
                Name = texture.Name,
                Type = txtType.ToString(),
                MipMapCount = texture.Levels,
                Compression = texture.Format.ToString()
            };
        }

        var drawableModels = yddFile.Drawables.First().DrawableModels;
        foreach (GDrawableDetails.DetailLevel detailLevel in Enum.GetValues(typeof(GDrawableDetails.DetailLevel)))
        {
            var model = detailLevel switch
            {
                GDrawableDetails.DetailLevel.High => drawableModels.High,
                GDrawableDetails.DetailLevel.Med => drawableModels.Med,
                GDrawableDetails.DetailLevel.Low => drawableModels.Low,
                _ => null
            };

            if (model != null)
            {
                details.AllModels[detailLevel] = new GDrawableModel
                {
                    PolyCount = (int)model.Sum(y => y.Geometries.Sum(g => g.IndicesCount / 3))
                };
            }
        }

        details.Validate();
        return details;
    }

    public async Task CheckForDuplicate()
    {
        if (!SettingsHelper.Instance.DisplayHashDuplicate) return;
        if (Details == null) return;
        await _semaphoreDublicateCheck.WaitAsync();
        List<Task> promises = [];
        try
        {
            IsDuplicate = false;
            if (Details.Hash != "")
            {
                var sexHashes = Sex ? hashes.Item1 : hashes.Item2;
                if (sexHashes.TryGetValue(Details.Hash, out List<GDrawable>? duplicates))
                {
                    if (!duplicates.Contains(this))
                    {
                        duplicates.Add(this);
                        foreach (GDrawable drawable in duplicates)
                        {
                            if (drawable == this) continue;
                            promises.Add(drawable.CheckForDuplicate());
                        }
                    }
                    if (duplicates.Any(h => h != this))
                    {
                        IsDuplicate = true;
                        IsDuplicateNameSetter = duplicates.FindAll(dup => dup != this);
                    }
                }
                else
                {
                    sexHashes.Add(Details.Hash, [this]);
                }
            }
        }
        finally
        {
            _semaphoreDublicateCheck.Release();
            await Task.WhenAll(promises);
        }

        foreach (GTexture texture in Textures)
        {
            await texture.CheckForDuplicate(Details.Hash, Sex);
        }
    }

    public async Task RemoveDuplicate()
    {
        if (!SettingsHelper.Instance.DisplayHashDuplicate) return;
        await DrawableDetailsTask;
        if (Details == null) return;
        await _semaphoreDublicateCheck.WaitAsync();
        List<Task> promises = [];
        try
        {
            var sexHashes = Sex ? hashes.Item1 : hashes.Item2;
            if (sexHashes.TryGetValue(Details.Hash, out List<GDrawable>? duplicates))
            {
                duplicates.Remove(this);
                var oldList = duplicates;

                // refresh duplicates
                sexHashes.Remove(Details.Hash);
                foreach (GDrawable duplicate in oldList)
                {
                    promises.Add(duplicate.CheckForDuplicate());
                }
            }
            foreach (GTexture texture in Textures)
            {
                promises.Add(texture.RemoveDuplicate(Details.Hash, Sex));
            }
        }
        finally
        {
            _semaphoreDublicateCheck.Release();
            await Task.WhenAll(promises);
        }
    }
}
