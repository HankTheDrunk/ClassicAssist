﻿using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Assistant;
using ClassicAssist.Data;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Macros;
using ClassicAssist.Data.Macros.Commands;
using ClassicAssist.Misc;
using ClassicAssist.Resources;
using ClassicAssist.UI.ViewModels.Macros;
using ClassicAssist.UI.Views;
using ClassicAssist.UI.Views.Macros;
using ClassicAssist.UO;
using ClassicAssist.UO.Objects;
using ICSharpCode.AvalonEdit.Document;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.UI.ViewModels
{
    public class MacrosTabViewModel : HotkeySettableViewModel<MacroEntry>, ISettingProvider
    {
        private CancellationTokenSource _cancellationToken;
        private int _caretPosition;
        private ICommand _clearHotkeyCommand;
        private MacroEntry _currentMacro;
        private TextDocument _document;
        private RelayCommand _executeCommand;
        private ICommand _inspectObjectCommand;
        private bool _isRecording;
        private bool _isRunning;
        private MacroInvoker _macroInvoker;
        private RelayCommand _newMacroCommand;
        private ICommand _recordCommand;
        private RelayCommand _removeMacroCommand;
        private MacroEntry _selectedItem;
        private ICommand _showActiveObjectsWindowCommand;
        private ICommand _showCommandsCommand;
        private ICommand _stopCommand;

        public MacrosTabViewModel() : base( Strings.Macros )
        {
            Engine.DisconnectedEvent += OnDisconnectedEvent;

            MacroManager manager = MacroManager.GetInstance();

            manager.IsRecording = () => _isRecording;
            manager.InsertDocument = str => { _dispatcher.Invoke( () => { SelectedItem.Macro += str; } ); };
            manager.Items = Items;
            manager.IsPlaying = () => _isRunning;
            manager.CurrentMacro = () => _currentMacro;
        }

        public int CaretPosition
        {
            get => _caretPosition;
            set => SetProperty( ref _caretPosition, value );
        }

        public ICommand ClearHotkeyCommand =>
            _clearHotkeyCommand ?? ( _clearHotkeyCommand = new RelayCommand( ClearHotkey, o => SelectedItem != null ) );

        public TextDocument Document
        {
            get => _document;
            set => SetProperty( ref _document, value );
        }

        public RelayCommand ExecuteCommand =>
            _executeCommand ??
            ( _executeCommand = new RelayCommand( Execute, o => !IsRunning && SelectedItem != null ) );

        public ICommand InspectObjectCommand =>
            _inspectObjectCommand ??
            ( _inspectObjectCommand = new RelayCommandAsync( InspectObject, o => Engine.Connected ) );

        public bool IsRecording
        {
            get => _isRecording;
            set => SetProperty( ref _isRecording, value );
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty( ref _isRunning, value );
        }

        public RelayCommand NewMacroCommand =>
            _newMacroCommand ?? ( _newMacroCommand = new RelayCommand( NewMacro, o => !IsRunning ) );

        public ICommand RecordCommand =>
            _recordCommand ?? ( _recordCommand = new RelayCommand( Record, o => SelectedItem != null ) );

        public string RecordLabel => IsRecording ? Strings.Stop : Strings.Record;

        public RelayCommand RemoveMacroCommand =>
            _removeMacroCommand ?? ( _removeMacroCommand =
                new RelayCommand( RemoveMacro, o => !IsRunning && SelectedItem != null ) );

        public MacroEntry SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        public ICommand ShowActiveObjectsWindowCommand =>
            _showActiveObjectsWindowCommand ?? ( _showActiveObjectsWindowCommand =
                new RelayCommand( ShowActiveObjectsWindow, o => true ) );

        public ICommand ShowCommandsCommand =>
            _showCommandsCommand ?? ( _showCommandsCommand = new RelayCommand( ShowCommands, o => true ) );

        public ICommand StopCommand => _stopCommand ?? ( _stopCommand = new RelayCommand( Stop, o => IsRunning ) );

        public void Serialize( JObject json )
        {
            JObject macros = new JObject();

            JArray macroArray = new JArray();

            foreach ( MacroEntry macroEntry in Items )
            {
                JObject entry = new JObject
                {
                    { "Name", macroEntry.Name },
                    { "Loop", macroEntry.Loop },
                    { "DoNotAutoInterrupt", macroEntry.DoNotAutoInterrupt },
                    { "Macro", macroEntry.Macro },
                    { "PassToUO", macroEntry.PassToUO },
                    { "Keys", macroEntry.Hotkey.ToJObject() }
                };

                macroArray.Add( entry );
            }

            macros.Add( "Macros", macroArray );

            JArray aliasArray = new JArray();

            foreach ( JObject entry in AliasCommands.GetAllAliases()
                .Select( kvp => new JObject { { "Name", kvp.Key }, { "Value", kvp.Value } } ) )
            {
                aliasArray.Add( entry );
            }

            macros.Add( "Alias", aliasArray );

            json?.Add( "Macros", macros );
        }

        public void Deserialize( JObject json, Options options )
        {
            JToken config = json?["Macros"];

            if ( config == null )
            {
                return;
            }

            if ( config["Macros"] != null )
            {
                foreach ( JToken token in config["Macros"] )
                {
                    MacroEntry entry = new MacroEntry
                    {
                        Name = GetJsonValue( token, "Name", string.Empty ),
                        Loop = GetJsonValue( token, "Loop", false ),
                        DoNotAutoInterrupt = GetJsonValue( token, "DoNotAutoInterrupt", false ),
                        Macro = GetJsonValue( token, "Macro", string.Empty ),
                        PassToUO = GetJsonValue( token, "PassToUO", true ),
                        Hotkey = new ShortcutKeys( token["Keys"] )
                    };

                    entry.Action = hks => Execute( entry );
                    entry.ActionSync = macroEntry => ExecuteSync( entry );

                    Items.Add( entry );
                }
            }

            if ( config["Alias"] != null )
            {
                foreach ( JToken token in config["Alias"] )
                {
                    AliasCommands.SetAlias( token["Name"].ToObject<string>(), token["Value"].ToObject<int>() );
                }
            }
        }

        private void ExecuteSync( MacroEntry entry )
        {
            _dispatcher.Invoke( () => IsRunning = true );
            _dispatcher.Invoke( () => SelectedItem = entry );

            _currentMacro = entry;

            _macroInvoker = new MacroInvoker( entry );
            _macroInvoker.StoppedEvent += () =>
            {
                _currentMacro = null;
                _dispatcher.Invoke( () => IsRunning = false );
            };

            _macroInvoker.ExceptionEvent += exception =>
            {
                Commands.SystemMessage( string.Format( Strings.Macro_error___0_, exception.Message ) );
            };

            _cancellationToken = new CancellationTokenSource();

            _macroInvoker.ExecuteSync( entry.Loop, _cancellationToken.Token );
        }

        private static void ShowActiveObjectsWindow( object obj )
        {
            ActiveObjectsWindow window = new ActiveObjectsWindow();
            window.Show();
        }

        private void OnDisconnectedEvent()
        {
            _macroInvoker?.Stop();
        }

        private void ClearHotkey( object obj )
        {
            if ( !( obj is MacroEntry entry ) )
            {
                return;
            }

            entry.Hotkey = ShortcutKeys.Default;
        }

        private static async Task InspectObject( object arg )
        {
            int serial = await Commands.GetTargeSerialAsync( Strings.Target_object___ );

            if ( serial > 0 )
            {
                Entity entity = UOMath.IsMobile( serial )
                    ? (Entity) Engine.Mobiles.GetMobile( serial )
                    : Engine.Items.GetItem( serial );

                if ( entity == null )
                {
                    return;
                }

                ObjectInspectorWindow window =
                    new ObjectInspectorWindow { DataContext = new ObjectInspectorViewModel( entity ) };

                window.Show();
            }
        }

        private void NewMacro( object obj )
        {
            int count = Items.Count;

            MacroEntry macro = new MacroEntry { Name = $"Macro-{count + 1}", Macro = string.Empty };

            macro.Action = hks => Execute( macro );
            macro.ActionSync = macroEntry => ExecuteSync( macro );

            Items.Add( macro );

            SelectedItem = macro;
        }

        private void RemoveMacro( object obj )
        {
            if ( obj is MacroEntry entry )
            {
                Items.Remove( entry );
            }
        }

        private void Stop( object obj )
        {
            _cancellationToken?.Cancel();
            _macroInvoker.Stop();
            IsRunning = false;
        }

        private void Execute( object obj )
        {
            if ( !( obj is MacroEntry entry ) )
            {
                return;
            }

            if ( _currentMacro != null && _currentMacro.DoNotAutoInterrupt && _currentMacro == entry )
            {
                return;
            }

            if ( IsRunning )
            {
                Stop( _currentMacro );
            }

            _dispatcher.Invoke( () => IsRunning = true );
            _dispatcher.Invoke( () => SelectedItem = entry );

            _currentMacro = entry;
            _currentMacro.Stop = () => Stop( _currentMacro );

            _macroInvoker = new MacroInvoker( entry );
            _macroInvoker.StoppedEvent += () =>
            {
                if ( entry.Loop && !_macroInvoker.IsFaulted && IsRunning )
                {
                    ExecuteSync( entry );
                    return;
                }

                _currentMacro = null;
                _dispatcher.Invoke( () => IsRunning = false );
            };

            _macroInvoker.ExceptionEvent += exception =>
            {
                Commands.SystemMessage( string.Format( Strings.Macro_error___0_, exception.Message ) );
            };

            _macroInvoker.Execute();
        }

        private void ShowCommands( object obj )
        {
            MacrosCommandWindow window = new MacrosCommandWindow { DataContext = new MacrosCommandViewModel( this ) };
            window.ShowDialog();
        }

        private void Record( object obj )
        {
            if ( IsRecording )
            {
                IsRecording = false;
                NotifyPropertyChanged( nameof( RecordLabel ) );
                return;
            }

            IsRecording = true;
            NotifyPropertyChanged( nameof( RecordLabel ) );
        }
    }
}