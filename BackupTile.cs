using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleMirrorBackup;

public sealed class BackupTile : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _title = "Neue Kachel";
    private int _order;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, string.IsNullOrWhiteSpace(value) ? "Unbenannte Kachel" : value.Trim());
    }

    public int Order
    {
        get => _order;
        set => SetField(ref _order, value);
    }

    public BackupTile Clone()
    {
        return new BackupTile
        {
            Id = Id,
            Title = Title,
            Order = Order
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}