using dockus.Core.Models;
using System.Collections.ObjectModel;

namespace dockus.Dock;

/// <summary>
/// Interface for managing dock behavior and window interactions
/// </summary>
public interface IDockManager : IDisposable
{
    /// <summary>
    /// Collection of pinned application items
    /// </summary>
    ObservableCollection<WindowItem> PinnedItems { get; }

    /// <summary>
    /// Collection of active unpinned window items
    /// </summary>
    ObservableCollection<WindowItem> ActiveUnpinnedItems { get; }

    /// <summary>
    /// Gets whether the Windows taskbar is currently hidden
    /// </summary>
    bool IsTaskbarHidden { get; }

    /// <summary>
    /// Initialize the dock manager and start all services
    /// </summary>
    void Initialize();

    /// <summary>
    /// Handle window size changes
    /// </summary>
    void OnWindowSizeChanged();

    /// <summary>
    /// Handle mouse entering the dock area
    /// </summary>
    void OnMouseEnter();

    /// <summary>
    /// Handle mouse leaving the dock area
    /// </summary>
    void OnMouseLeave();

    /// <summary>
    /// Handle icon click events
    /// </summary>
    /// <param name="item">The window item that was clicked</param>
    void OnIconClick(WindowItem item);

    /// <summary>
    /// Pin an item to the dock
    /// </summary>
    /// <param name="item">The item to pin</param>
    void PinItem(WindowItem item);

    /// <summary>
    /// Unpin an item from the dock
    /// </summary>
    /// <param name="item">The item to unpin</param>
    void UnpinItem(WindowItem item);

    /// <summary>
    /// Save pinned applications to persistence
    /// </summary>
    void SavePinnedApps();

    /// <summary>
    /// Toggle Windows taskbar visibility
    /// </summary>
    void ToggleTaskbar();
}