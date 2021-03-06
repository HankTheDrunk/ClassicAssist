﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Assistant;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.Resources;
using ClassicAssist.UI.Views;
using ClassicAssist.UO;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.UI.ViewModels
{
    public class EntityCollectionViewerViewModel : BaseViewModel
    {
        private ICommand _cancelActionCommand;
        private CancellationTokenSource _cancellationToken;
        private ItemCollection _collection;
        private ICommand _contextMoveToBackpackCommand;
        private ICommand _contextMoveToContainerCommand;
        private ICommand _contextUseItemCommand;
        private ObservableCollection<EntityCollectionData> _entities;
        private bool _isPerformingAction;
        private ICommand _itemDoubleClickCommand;
        private ICommand _refreshCommand;

        private ObservableCollection<EntityCollectionData> _selectedItems =
            new ObservableCollection<EntityCollectionData>();

        private bool _showProperties;
        private string _statusLabel;
        private ICommand _togglePropertiesCommand;
        private bool _topmost;

        public EntityCollectionViewerViewModel()
        {
            Entities = new ObservableCollection<EntityCollectionData>
            {
                new EntityCollectionData { Entity = new Item( 1 ) },
                new EntityCollectionData { Entity = new Item( 2 ) }
            };
        }

        public EntityCollectionViewerViewModel( ItemCollection collection )
        {
            _collection = collection;

            Entities = new ObservableCollection<EntityCollectionData>( collection.ToEntityCollectionData() );

            SelectedItems.CollectionChanged += ( sender, args ) =>
            {
                if ( !( sender is ObservableCollection<EntityCollectionData> si ) )
                {
                    return;
                }

                StatusLabel = string.Format( Strings._0__items___1__selected___2__total_amount, Entities.Count,
                    si.Count, si.Select( i => i.Entity ).Where( i => i is Item ).Cast<Item>().Sum( i => i.Count ) );
            };
        }

        public ICommand CancelActionCommand =>
            _cancelActionCommand ?? ( _cancelActionCommand = new RelayCommandAsync( CancelAction,
                o => _cancellationToken != null ) );

        public ICommand ContextMoveToBackpackCommand =>
            _contextMoveToBackpackCommand ?? ( _contextMoveToBackpackCommand =
                new RelayCommandAsync( ContextMoveToBackpack,
                    o => SelectedItems != null && !IsPerformingAction ) );

        public ICommand ContextMoveToContainerCommand =>
            _contextMoveToContainerCommand ?? ( _contextMoveToContainerCommand =
                new RelayCommandAsync( ContextMoveToContainer,
                    o => SelectedItems != null && !IsPerformingAction ) );

        public ICommand ContextUseItemCommand =>
            _contextUseItemCommand ?? ( _contextUseItemCommand =
                new RelayCommandAsync( ContextUseItem, o => SelectedItems != null && !IsPerformingAction ) );

        public ObservableCollection<EntityCollectionData> Entities
        {
            get => _entities;
            set => SetProperty( ref _entities, value );
        }

        public bool IsPerformingAction
        {
            get => _isPerformingAction;
            set => SetProperty( ref _isPerformingAction, value );
        }

        public ICommand ItemDoubleClickCommand =>
            _itemDoubleClickCommand ?? ( _itemDoubleClickCommand = new RelayCommand( ItemDoubleClick, o => true ) );

        public ICommand RefreshCommand =>
            _refreshCommand ?? ( _refreshCommand = new RelayCommand( Refresh, o => _collection?.Serial != 0 ) );

        public ObservableCollection<EntityCollectionData> SelectedItems
        {
            get => _selectedItems;
            set => SetProperty( ref _selectedItems, value );
        }

        public bool ShowProperties
        {
            get => _showProperties;
            set => SetProperty( ref _showProperties, value );
        }

        public string StatusLabel
        {
            get => _statusLabel;
            set => SetProperty( ref _statusLabel, value );
        }

        public ICommand TogglePropertiesCommand =>
            _togglePropertiesCommand ?? ( _togglePropertiesCommand = new RelayCommand( ToggleProperties, o => true ) );

        public bool Topmost
        {
            get => _topmost;
            set => SetProperty( ref _topmost, value );
        }

        private void Refresh( object obj )
        {
            if ( Engine.Items.GetItem( _collection.Serial, out Item item ) )
            {
                if ( item.Container != null )
                {
                    _collection = item.Container;
                }
            }

            Entities = new ObservableCollection<EntityCollectionData>( _collection.ToEntityCollectionData() );
        }

        private async Task ContextUseItem( object arg )
        {
            _cancellationToken = new CancellationTokenSource();

            try
            {
                IsPerformingAction = true;

                await Task.Run( () =>
                {
                    int[] items = SelectedItems.Select( i => i.Entity.Serial ).ToArray();

                    foreach ( int item in items )
                    {
                        ObjectCommands.UseObject( item );

                        if ( _cancellationToken.Token.IsCancellationRequested )
                        {
                            return;
                        }
                    }
                } );
            }
            finally
            {
                IsPerformingAction = false;
            }
        }

        private async Task ContextMoveToContainer( object arg )
        {
            _cancellationToken = new CancellationTokenSource();

            int serial = 0;

            if ( arg is int s )
            {
                serial = s;
            }

            if ( serial == 0 )
            {
                serial = await Commands.GetTargeSerialAsync( Strings.Target_container___ );
            }

            if ( serial == 0 )
            {
                Commands.SystemMessage( Strings.Invalid_container___ );
                return;
            }

            try
            {
                IsPerformingAction = true;

                int[] items = SelectedItems.Select( i => i.Entity.Serial ).ToArray();

                foreach ( int item in items )
                {
                    await Commands.DragDropAsync( item, -1, serial );

                    if ( _cancellationToken.Token.IsCancellationRequested )
                    {
                        return;
                    }
                }
            }
            finally
            {
                IsPerformingAction = false;
            }
        }

        private async Task ContextMoveToBackpack( object arg )
        {
            await ContextMoveToContainer( Engine.Player.Backpack.Serial );
        }

        private async Task CancelAction( object arg )
        {
            _cancellationToken?.Cancel();

            await Task.CompletedTask;
        }

        private void ToggleProperties( object obj )
        {
            if ( !( obj is bool showProperties ) )
            {
                return;
            }

            ShowProperties = showProperties;
        }

        private static void ItemDoubleClick( object obj )
        {
            if ( !( obj is EntityCollectionData ecd ) )
            {
                return;
            }

            ObjectInspectorWindow window =
                new ObjectInspectorWindow { DataContext = new ObjectInspectorViewModel( ecd.Entity ) };

            window.ShowDialog();
        }
    }

    public class EntityCollectionData
    {
        public BitmapSource Bitmap => Art.GetStatic( Entity.ID, Entity.Hue ).ToBitmapSource();
        public Entity Entity { get; set; }
        public string FullName => GetProperties( Entity );
        public string Name => Entity.Name;

        private static string GetProperties( Entity entity )
        {
            return entity.Properties == null
                ? entity.Name
                : entity.Properties.Aggregate( "",
                    ( current, entityProperty ) => current + entityProperty.Text + "\r\n" ).TrimTrailingNewLine();
        }
    }

    public static class ExtensionMethods
    {
        public static string TrimTrailingNewLine( this string s )
        {
            return s.TrimEnd( '\r', '\n' );
        }

        public static List<EntityCollectionData> ToEntityCollectionData( this ItemCollection itemCollection )
        {
            if ( itemCollection == null )
            {
                return new List<EntityCollectionData>();
            }

            Item[] items = itemCollection.GetItems();

            IEnumerable<Item> noNames = items.Where( i => string.IsNullOrEmpty( i.Name ) );

            foreach ( Item item in noNames )
            {
                item.Name = $"0x{item.Serial:x8}";
            }

            return items.Select( item => new EntityCollectionData { Entity = item } ).ToList();
        }
    }
}