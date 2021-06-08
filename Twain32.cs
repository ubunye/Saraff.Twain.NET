/* Этот файл является частью библиотеки Saraff.Twain.NET
 * © SARAFF SOFTWARE (Кирножицкий Андрей), 2011.
 * Saraff.Twain.NET - свободная программа: вы можете перераспространять ее и/или
 * изменять ее на условиях Меньшей Стандартной общественной лицензии GNU в том виде,
 * в каком она была опубликована Фондом свободного программного обеспечения;
 * либо версии 3 лицензии, либо (по вашему выбору) любой более поздней
 * версии.
 * Saraff.Twain.NET распространяется в надежде, что она будет полезной,
 * но БЕЗО ВСЯКИХ ГАРАНТИЙ; даже без неявной гарантии ТОВАРНОГО ВИДА
 * или ПРИГОДНОСТИ ДЛЯ ОПРЕДЕЛЕННЫХ ЦЕЛЕЙ. Подробнее см. в Меньшей Стандартной
 * общественной лицензии GNU.
 * Вы должны были получить копию Меньшей Стандартной общественной лицензии GNU
 * вместе с этой программой. Если это не так, см.
 * <http://www.gnu.org/licenses/>.)
 * 
 * This file is part of Saraff.Twain.NET.
 * © SARAFF SOFTWARE (Kirnazhytski Andrei), 2011.
 * Saraff.Twain.NET is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * Saraff.Twain.NET is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 * You should have received a copy of the GNU Lesser General Public License
 * along with Saraff.Twain.NET. If not, see <http://www.gnu.org/licenses/>.
 * 
 * PLEASE SEND EMAIL TO:  twain@saraff.ru.
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Saraff.Twain
{
    /// <summary>
    ///     Provides the ability to work with TWAIN sources.
    ///     <para xml:lang="ru">Обеспечивает возможность работы с TWAIN-источниками.</para>
    /// </summary>
    [ToolboxBitmap(typeof(Twain32), "Resources.scanner.bmp")]
    [DebuggerDisplay(
        "ProductName = {_appid.ProductName.Value}, Version = {_appid.Version.Info}, DS = {_srcds.ProductName}")]
    [DefaultEvent("AcquireCompleted")]
    [DefaultProperty("AppProductName")]
    public sealed class Twain32 : Component
    {
        /// <summary>
        ///     State flags.
        ///     <para xml:lang="ru">Флаги состояния.</para>
        /// </summary>
        [Flags]
        public enum TwainStateFlag
        {
            /// <summary>
            ///     The DSM open.
            /// </summary>
            DsmOpen = 0x1,

            /// <summary>
            ///     The ds open.
            /// </summary>
            DsOpen = 0x2,

            /// <summary>
            ///     The ds enabled.
            /// </summary>
            DsEnabled = 0x4,

            /// <summary>
            ///     The ds ready.
            /// </summary>
            DsReady = 0x08
        }

        private TwIdentity _appid; //application identifier / идентификатор приложения.
        private readonly CallBackProc _callbackProc;
        private TwainCapabilities _capabilities;
        private readonly IContainer _components = new Container();

        private ApplicationContext
            _context; //application context. used if there is no main message processing cycle / контекст приложения. используется в случае отсутствия основного цикла обработки сообщений.

        private DsmEntry _dsmEntry;
        private readonly MessageFilter _filter; //WIN32 event filter / фильтр событий WIN32
        private IntPtr _hTwainDll; //module descriptor twain_32.dll / дескриптор модуля twain_32.dll
        private IntPtr _hwnd; //handle to the parent window / дескриптор родительского окна.
        private readonly Collection<SealedImage> _images = new Collection<SealedImage>();

        private readonly bool _isTwain2Enable = IntPtr.Size != 4 || Environment.OSVersion.Platform == PlatformID.Unix ||
                                                Environment.OSVersion.Platform == PlatformID.MacOSX;

        private TwIdentity[]
            _sources = new TwIdentity[0]; //an array of available data sources / массив доступных источников данных.

        private TwIdentity _srcds; //identifier of the current data source / идентификатор текущего источника данных.
        private TwainStateFlag _twainState;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Twain32" /> class.
        /// </summary>
        public Twain32()
        {
            _srcds = new TwIdentity { Id = 0 };
            _filter = new MessageFilter(this);
            ShowUi = true;
            DisableAfterAcquire = true;
            Palette = new TwainPalette(this);
            _callbackProc = _TwCallbackProc;
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    break;
                default:
                    var window = new Form();
                    _components.Add(window);
                    _hwnd = window.Handle;
                    break;
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Twain32" /> class.
        /// </summary>
        /// <param name="container">The container.</param>
        public Twain32(IContainer container) : this()
        {
            container.Add(this);
        }

        /// <summary>
        ///     Returns the number of scanned images.
        ///     <para xml:lang="ru">Возвращает количество отсканированных изображений.</para>
        /// </summary>
        [Browsable(false)]
        public int ImageCount => _images.Count;

        /// <summary>
        ///     Gets or sets a value indicating the need to deactivate the data source after receiving the image.
        ///     <para xml:lang="ru">
        ///         Возвращает или устанавливает значение, указывающее на необходимость деактивации источника
        ///         данных после получения изображения.
        ///     </para>
        /// </summary>
        [DefaultValue(true)]
        [Category("Behavior")]
        [Description(
            "Gets or sets a value indicating the need to deactivate the data source after receiving the image.")]
        public bool DisableAfterAcquire { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether to use TWAIN 2.0.
        ///     <para xml:lang="ru">Возвращает или устанавливает значение, указывающее на необходимость использования TWAIN 2.0.</para>
        /// </summary>
        [DefaultValue(false)]
        [Category("Behavior")]
        [Description(
            "Gets or sets a value indicating whether to use TWAIN 2.0.")]
        public bool IsTwain2Enable
        {
            get => _isTwain2Enable;
            set
            {
                if ((TwainState & TwainStateFlag.DsmOpen) != 0)
                    throw new InvalidOperationException("DSM already opened.");
                if (IntPtr.Size != 4 && !value)
                    throw new InvalidOperationException("In x64 mode only TWAIN 2.x enabled.");
                if (Environment.OSVersion.Platform == PlatformID.Unix && !value)
                    throw new InvalidOperationException("On UNIX platform only TWAIN 2.x enabled.");
                if (Environment.OSVersion.Platform == PlatformID.MacOSX && !value)
                    throw new InvalidOperationException("On MacOSX platform only TWAIN 2.x enabled.");
                if (_isTwain2Enable == value)
                    AppId.SupportedGroups |= TwDG.APP2;
                else
                    AppId.SupportedGroups &= ~TwDG.APP2;
                AppId.ProtocolMajor = (ushort)(_isTwain2Enable ? 2 : 1);
                AppId.ProtocolMinor = (ushort)(_isTwain2Enable ? 3 : 9);
            }
        }

        /// <summary>
        ///     Returns true if DSM supports TWAIN 2.0; otherwise false.
        ///     <para xml:lang="ru">Возвращает истину, если DSM поддерживает TWAIN 2.0; иначе лож.</para>
        /// </summary>
        [Browsable(false)]
        public bool IsTwain2Supported
        {
            get
            {
                if ((TwainState & TwainStateFlag.DsmOpen) == 0) throw new InvalidOperationException("DSM is not open.");
                return (AppId.SupportedGroups & TwDG.DSM2) != 0;
            }
        }

        /// <summary>
        ///     Gets or sets the value of the status flags.
        ///     <para xml:lang="ru">Возвращает или устанавливает значение флагов состояния.</para>
        /// </summary>
        private TwainStateFlag TwainState
        {
            get => _twainState;
            set
            {
                if (_twainState == value)
                    return;

                _twainState = value;
                TwainStateChanged?.Invoke(this, new TwainStateEventArgs(_twainState));
            }
        }

        /// <summary>
        ///     Releases the unmanaged resources used by the <see cref="T:System.ComponentModel.Component" /> and optionally
        ///     releases the managed resources.
        /// </summary>
        /// <param name="disposing">
        ///     true to release both managed and unmanaged resources; false to release only unmanaged
        ///     resources.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseDsm();
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        break;
                    default:
                        _filter.Dispose();
                        break;
                }

                _UnloadDSM();
                _components?.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        ///     Opens the data source manager
        ///     <para xml:lang="ru">Открывает менеджер источников данных.</para>
        /// </summary>
        /// <returns>True if the operation was successful; otherwise, false.
        ///     <para xml:lang="ru">Истина, если операция прошла удачно; иначе, лож.</para>
        /// </returns>
        public bool OpenDsm()
        {
            if ((TwainState & TwainStateFlag.DsmOpen) != 0)
                return (TwainState & TwainStateFlag.DsmOpen) != 0;

            #region We load DSM, we receive the address of the entry point DSM_Entry and we bring it to the appropriate delegates / Загружаем DSM, получаем адрес точки входа DSM_Entry и приводим ее к соответствующим делегатам

            var twainDsm = Path.ChangeExtension(Path.Combine(Environment.SystemDirectory, "TWAINDSM"), ".dll");
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    _dsmEntry = DsmEntry.Create(IntPtr.Zero);
                    try
                    {
                        if (_dsmEntry.DsmRaw == null) throw new InvalidOperationException("Can't load DSM.");
                    }
                    catch (Exception ex)
                    {
                        throw new TwainException("Can't load DSM.", ex);
                    }

                    break;
                default:
                    _hTwainDll = _Platform.Load(File.Exists(twainDsm) && IsTwain2Enable
                        ? twainDsm
                        : Path.ChangeExtension(Path.Combine(Environment.SystemDirectory, "..\\twain_32"), ".dll"));
                    if (Parent != null) _hwnd = Parent.Handle;
                    if (_hTwainDll != IntPtr.Zero)
                    {
                        var pDsmEntry = _Platform.GetProcAddr(_hTwainDll, "DSM_Entry");
                        if (pDsmEntry != IntPtr.Zero)
                        {
                            _dsmEntry = DsmEntry.Create(pDsmEntry);
                            _Memory._SetEntryPoints(null);
                        }
                        else
                        {
                            throw new TwainException("Can't find DSM_Entry entry point.");
                        }
                    }
                    else
                    {
                        throw new TwainException("Can't load DSM.");
                    }

                    break;
            }

            #endregion

            for (var rc = _dsmEntry.DsmParent(AppId, IntPtr.Zero, TwDG.Control, TwDAT.Parent, TwMSG.OpenDSM, ref _hwnd);
                rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
            TwainState |= TwainStateFlag.DsmOpen;

            var entry = new TwEntryPoint();
            if (IsTwain2Supported)
            {
                for (var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.EntryPoint, TwMSG.Get, ref entry);
                    rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
                _Memory._SetEntryPoints(entry);
            }

            _GetAllSorces();
            return (TwainState & TwainStateFlag.DsmOpen) != 0;
        }

        /// <summary>
        ///     Displays a dialog box for selecting a data source.
        ///     <para xml:lang="ru">Отображает диалоговое окно для выбора источника данных.</para>
        /// </summary>
        /// <returns>True if the operation was successful; otherwise, false.
        ///     <para xml:lang="ru">Истина, если операция прошла удачно; иначе, лож.</para>
        /// </returns>
        public bool SelectSource()
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
                throw new NotSupportedException(
                    "DG_CONTROL / DAT_IDENTITY / MSG_USERSELECT is not available on Linux.");

            var src = new TwIdentity();
            if ((TwainState & TwainStateFlag.DsOpen) != 0)
                return false;

            if ((TwainState & TwainStateFlag.DsmOpen) == 0)
            {
                OpenDsm();
                if ((TwainState & TwainStateFlag.DsmOpen) == 0) return false;
            }

            for (var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.UserSelect, ref src);
                rc != TwRC.Success;)
            {
                if (rc == TwRC.Cancel) return false;
                throw new TwainException(_GetTwainStatus(), rc);
            }

            _srcds = src;
            return true;
        }

        /// <summary>
        ///     Opens a data source.
        ///     <para xml:lang="ru">Открывает источник данных.</para>
        /// </summary>
        /// <returns>True if the operation was successful; otherwise, false.
        ///     <para xml:lang="ru">Истина, если операция прошла удачно; иначе, лож.</para>
        /// </returns>
        public bool OpenDataSource()
        {
            if ((TwainState & TwainStateFlag.DsmOpen) == 0 || (TwainState & TwainStateFlag.DsOpen) != 0)
                return (TwainState & TwainStateFlag.DsOpen) != 0;

            for (var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.OpenDS, ref _srcds);
                rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
            TwainState |= TwainStateFlag.DsOpen;

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    _RegisterCallback();
                    break;
                default:
                    if (IsTwain2Supported && (_srcds.SupportedGroups & TwDG.DS2) != 0) _RegisterCallback();
                    break;
            }

            return (TwainState & TwainStateFlag.DsOpen) != 0;
        }

        /// <summary>
        ///     Registers a data source event handler.
        ///     <para xml:lang="ru">Регестрирует обработчик событий источника данных.</para>
        /// </summary>
        private void _RegisterCallback()
        {
            var callback = new TwCallback2
            {
                CallBackProc = _callbackProc
            };
            var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.Callback2, TwMSG.RegisterCallback,
                ref callback);
            if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
        }

        /// <summary>
        ///     Activates a data source.
        ///     <para xml:lang="ru">Активирует источник данных.</para>
        /// </summary>
        /// <returns>True if the operation was successful; otherwise, false.
        ///     <para xml:lang="ru">Истина, если операция прошла удачно; иначе, лож.</para>
        /// </returns>
        private bool _EnableDataSource()
        {
            if ((TwainState & TwainStateFlag.DsOpen) == 0 || (TwainState & TwainStateFlag.DsEnabled) != 0)
                return (TwainState & TwainStateFlag.DsEnabled) != 0;

            var guif = new TwUserInterface
            {
                ShowUI = ShowUi,
                ModalUI = ModalUi,
                ParentHand = _hwnd
            };
            for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.UserInterface, TwMSG.EnableDS,
                    ref guif);
                rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
            if ((TwainState & TwainStateFlag.DsReady) != 0)
                TwainState &= ~TwainStateFlag.DsReady;
            else
                TwainState |= TwainStateFlag.DsEnabled;
            return (TwainState & TwainStateFlag.DsEnabled) != 0;
        }

        /// <summary>
        ///     Gets an image from a data source.
        ///     <para xml:lang="ru">Получает изображение с источника данных.</para>
        /// </summary>
        public void Acquire()
        {
            if (!OpenDsm())
                return;

            if (!OpenDataSource())
                return;

            if (!_EnableDataSource())
                return;

            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Unix:
                case PlatformID.MacOSX:
                    break;
                default:
                    if (!IsTwain2Supported || (_srcds.SupportedGroups & TwDG.DS2) == 0) _filter.SetFilter();
                    if (!Application.MessageLoop) Application.Run(_context = new ApplicationContext());
                    break;
            }
        }

        /// <summary>
        ///     Deactivates the data source.
        ///     <para xml:lang="ru">Деактивирует источник данных.</para>
        /// </summary>
        /// <returns>True if the operation was successful; otherwise, false.
        ///     <para xml:lang="ru">Истина, если операция прошла удачно; иначе, лож.</para>
        /// </returns>
        private void DisableDataSource()
        {
            if ((TwainState & TwainStateFlag.DsEnabled) == 0)
                return;

            try
            {
                var guif = new TwUserInterface
                {
                    ParentHand = _hwnd,
                    ShowUI = false
                };
                for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.UserInterface, TwMSG.DisableDS,
                        ref guif);
                    rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
            }
            finally
            {
                TwainState &= ~TwainStateFlag.DsEnabled;
                if (_context != null)
                {
                    _context.ExitThread();
                    _context.Dispose();
                    _context = null;
                }
            }
        }

        /// <summary>
        ///     Closes the data source.
        ///     <para xml:lang="ru">Закрывает источник данных.</para>
        /// </summary>
        /// <returns>True if the operation was successful; otherwise, false.
        ///     <para xml:lang="ru">Истина, если операция прошла удачно; иначе, лож.</para>
        /// </returns>
        private void CloseDataSource()
        {
            if ((TwainState & TwainStateFlag.DsOpen) == 0 || (TwainState & TwainStateFlag.DsEnabled) != 0)
                return;
            _images.Clear();
            for (var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.CloseDS, ref _srcds);
                rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
            TwainState &= ~TwainStateFlag.DsOpen;
        }

        /// <summary>
        ///     Closes the data source manager.
        ///     <para xml:lang="ru">Закрывает менежер источников данных.</para>
        /// </summary>
        /// <returns>True if the operation was successful; otherwise, false.
        ///     <para xml:lang="ru">Истина, если операция прошла удачно; иначе, лож.</para>
        /// </returns>
        private void CloseDsm()
        {
            if ((TwainState & TwainStateFlag.DsEnabled) != 0) DisableDataSource();
            if ((TwainState & TwainStateFlag.DsOpen) != 0) CloseDataSource();

            if ((TwainState & TwainStateFlag.DsmOpen) == 0 || (TwainState & TwainStateFlag.DsOpen) != 0)
                return;

            for (var rc =
                    _dsmEntry.DsmParent(AppId, IntPtr.Zero, TwDG.Control, TwDAT.Parent, TwMSG.CloseDSM, ref _hwnd);
                rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
            TwainState &= ~TwainStateFlag.DsmOpen;
            _UnloadDSM();
        }

        private void _UnloadDSM()
        {
            AppId = null;
            if (_hTwainDll == IntPtr.Zero)
                return;

            _Platform.Unload(_hTwainDll);
            _hTwainDll = IntPtr.Zero;
        }

        /// <summary>
        ///     Returns the scanned image.
        ///     <para xml:lang="ru">Возвращает отсканированое изображение.</para>
        /// </summary>
        /// <param name="index">Image index.
        ///     <para xml:lang="ru">Индекс изображения.</para>
        /// </param>
        /// <returns>Instance of the image.
        ///     <para xml:lang="ru">Экземпляр изображения.</para>
        /// </returns>
        public Image GetImage(int index)
        {
            return _images[index];
        }

        /// <summary>
        ///     Gets a description of all available data sources.
        ///     <para xml:lang="ru">Получает описание всех доступных источников данных.</para>
        /// </summary>
        private void _GetAllSorces()
        {
            var src = new List<TwIdentity>();
            var item = new TwIdentity();
            try
            {
                for (var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.GetFirst, ref item);
                    rc != TwRC.Success;)
                {
                    if (rc == TwRC.EndOfList) return;
                    throw new TwainException(_GetTwainStatus(), rc);
                }

                src.Add(item);
                while (true)
                {
                    item = new TwIdentity();
                    var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.GetNext, ref item);
                    if (rc == TwRC.Success)
                    {
                        src.Add(item);
                        continue;
                    }

                    if (rc == TwRC.EndOfList) break;
                    throw new TwainException(_GetTwainStatus(), rc);
                }

                for (var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.GetDefault, ref _srcds);
                    rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
            }
            finally
            {
                _sources = src.ToArray();
            }
        }

        /// <summary>
        ///     Returns the TWAIN status code.
        ///     <para xml:lang="ru">Возвращает код состояния TWAIN.</para>
        /// </summary>
        /// <returns></returns>
        private TwCC _GetTwainStatus()
        {
            var status = new TwStatus();
            _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.Status, TwMSG.Get, ref status);
            return status.ConditionCode;
        }

        /// <summary>
        ///     Returns a description of the received image.
        ///     <para xml:lang="ru">Возвращает описание полученного изображения.</para>
        /// </summary>
        /// <returns>Description of the image.
        ///     <para xml:lang="ru">Описание изображения.</para>
        /// </returns>
        private ImageInfo _GetImageInfo()
        {
            var imageInfo = new TwImageInfo();
            var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Image, TwDAT.ImageInfo, TwMSG.Get, ref imageInfo);
            if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
            return ImageInfo.FromTwImageInfo(imageInfo);
        }

        /// <summary>
        ///     Returns an extended description of the resulting image.
        ///     <para xml:lang="ru">Возвращает расширенного описание полученного изображения.</para>
        /// </summary>
        /// <param name="extInfo">A set of codes for the extended image description for which you want to get a description.
        ///     <para xml:lang="ru">Набор кодов расширенного описания изображения для которых требуется получить описание.</para>
        /// </param>
        /// <returns>Extended image description.
        ///     <para xml:lang="ru">Расширенное описание изображения.</para>
        /// </returns>
        private ExtImageInfo _GetExtImageInfo(TwEI[] extInfo)
        {
            var info = new TwInfo[extInfo.Length];
            for (var i = 0; i < extInfo.Length; i++) info[i] = new TwInfo { InfoId = extInfo[i] };
            var extImageInfo = TwExtImageInfo.ToPtr(info);
            try
            {
                var rc = _dsmEntry.DsRaw(AppId, _srcds, TwDG.Image, TwDAT.ExtImageInfo, TwMSG.Get, extImageInfo);
                if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
                return ExtImageInfo.FromPtr(extImageInfo);
            }
            finally
            {
                Marshal.FreeHGlobal(extImageInfo);
            }
        }

        #region Information of sorces

        /// <summary>
        ///     Gets or sets the index of the current data source.
        ///     <para xml:lang="ru">Возвращает или устанавливает индекс текущего источника данных.</para>
        /// </summary>
        [Browsable(false)]
        [ReadOnly(true)]
        public int SourceIndex
        {
            get
            {
                if ((TwainState & TwainStateFlag.DsmOpen) == 0)
                    return -1;

                int i;
                for (i = 0; i < _sources.Length; i++)
                    if (_sources[i].Equals(_srcds))
                        break;
                return i;
            }
            set
            {
                if ((TwainState & TwainStateFlag.DsmOpen) != 0)
                {
                    if ((TwainState & TwainStateFlag.DsOpen) == 0)
                        _srcds = _sources[value];
                    else
                        throw new TwainException("The data source is already open.");
                }
                else
                {
                    throw new TwainException("Data Source Manager is not open.");
                }
            }
        }

        /// <summary>
        ///     Returns the number of data sources.
        ///     <para xml:lang="ru">Возвращает количество источников данных.</para>
        /// </summary>
        [Browsable(false)]
        public int SourcesCount => _sources.Length;

        /// <summary>
        ///     Returns the manufacturer name of the data source at the specified index.
        /// </summary>
        /// <param name="index">Index.
        ///     <para xml:lang="ru">Индекс.</para>
        /// </param>
        /// <returns>The manufacturer name of the data source.
        ///     <para xml:lang="ru">Имя источника данных.</para>
        /// </returns>
        public string GetSourceManufacturerName(int index)
        {
            return _sources[index].Manufacturer;
        }

        /// <summary>
        ///     Returns the name of the data source at the specified index.
        ///     <para xml:lang="ru">Возвращает имя источника данных по указанному индексу.</para>
        /// </summary>
        /// <param name="index">Index.
        ///     <para xml:lang="ru">Индекс.</para>
        /// </param>
        /// <returns>The name of the data source.
        ///     <para xml:lang="ru">Имя источника данных.</para>
        /// </returns>
        public string GetSourceProductName(int index)
        {
            return _sources[index].ProductName;
        }

        /// <summary>
        ///     Returns the product family of the data source at the specified index.
        /// </summary>
        /// <param name="index">Index.
        ///     <para xml:lang="ru">Индекс.</para>
        /// </param>
        /// <returns>The product family of the data source.
        ///     <para xml:lang="ru">Имя источника данных.</para>
        /// </returns>
        public string GetSourceProductFamily(int index)
        {
            return _sources[index].ProductFamily;
        }

        /// <summary>
        ///     Gets a description of the specified source.
        ///     <para xml:lang="ru">Возвращает описание указанного источника. Gets the source identity.</para>
        /// </summary>
        /// <param name="index">Index.
        ///     <para xml:lang="ru">Индекс. The index.</para>
        /// </param>
        /// <returns>Description of the data source.
        ///     <para xml:lang="ru">Описание источника данных.</para>
        /// </returns>
        public Identity GetSourceIdentity(int index)
        {
            return new Identity(_sources[index]);
        }

        /// <summary>
        ///     Returns true if the specified source supports TWAIN 2.0; otherwise false.
        ///     <para xml:lang="ru">Возвращает истину, если указанный источник поддерживает TWAIN 2.0; иначе лож.</para>
        /// </summary>
        /// <param name="index">Index
        ///     <para xml:lang="ru">Индекс.</para>
        /// </param>
        /// <returns>True if the specified source supports TWAIN 2.0; otherwise false.
        ///     <para xml:lang="ru">Истина, если указанный источник поддерживает TWAIN 2.0; иначе лож.</para>
        /// </returns>
        public bool GetIsSourceTwain2Compatible(int index)
        {
            return (_sources[index].SupportedGroups & TwDG.DS2) != 0;
        }

        /// <summary>
        ///     Sets the specified data source as the default data source.
        ///     <para xml:lang="ru">Устанавливает указанный источник данных в качестве источника данных по умолчанию.</para>
        /// </summary>
        /// <param name="index">Index.
        ///     <para xml:lang="ru">Индекс.</para>
        /// </param>
        public void SetDefaultSource(int index)
        {
            if ((TwainState & TwainStateFlag.DsmOpen) != 0)
            {
                if ((TwainState & TwainStateFlag.DsOpen) == 0)
                {
                    var src = _sources[index];
                    var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.Set, ref src);
                    if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
                }
                else
                {
                    throw new TwainException(
                        "The data source is already open. You must first close the data source.");
                }
            }
            else
            {
                throw new TwainException("DSM is not open.");
            }
        }

        /// <summary>
        ///     Sets the specified data source as the data source.
        /// </summary>
        /// <param name="source">Source.
        ///     <para xml:lang="ru">Индекс.</para>
        /// </param>
        public void SetSource(string source)
        {
            if ((TwainState & TwainStateFlag.DsmOpen) != 0)
            {
                if ((TwainState & TwainStateFlag.DsOpen) == 0)
                {
                    var identities = new List<TwIdentity>();
                    foreach (var identity in _sources) identities.Add(identity);

                    var src = identities.Find(x => x.ProductName == source);
                    _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.Set, ref src);
                }
                else
                {
                    throw new TwainException("The data source is already open. You must first close the data source.");
                }
            }
            else
            {
                throw new TwainException("DSM is not open.");
            }
        }

        /// <summary>
        ///     Gets the default Data Source.
        /// </summary>
        /// <returns>Index of default Data Source.</returns>
        /// <exception cref="TwainException">
        ///     Не удалось найти источник данных по умолчанию.
        ///     or
        ///     DSM не открыт.
        /// </exception>
        public int GetDefaultSource()
        {
            var identity = new TwIdentity();
            if ((TwainState & TwainStateFlag.DsmOpen) == 0)
                throw new TwainException("DSM is not open.");

            for (var rc = _dsmEntry.DsmInvoke(AppId, TwDG.Control, TwDAT.Identity, TwMSG.GetDefault, ref identity);
                rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
            for (var i = 0; i < _sources.Length; i++)
                if (identity.Id == _sources[i].Id)
                    return i;
            throw new TwainException("Could not find default data source.");
        }

        #endregion

        #region Properties of source

        /// <summary>
        ///     Gets the application identifier.
        ///     <para xml:lang="ru">Возвращает идентификатор приложения.</para>
        /// </summary>
        [Browsable(false)]
        [ReadOnly(true)]
        private TwIdentity AppId
        {
            get
            {
                if (_appid != null)
                    return _appid;

                var asm = typeof(Twain32).Assembly;
                var asmName = new AssemblyName(asm.FullName);
                var version =
                    new Version(
                        ((AssemblyFileVersionAttribute)asm.GetCustomAttributes(typeof(AssemblyFileVersionAttribute),
                            false)[0]).Version);

                _appid = new TwIdentity
                {
                    Id = 0,
                    Version = new TwVersion
                    {
                        MajorNum = (ushort)version.Major,
                        MinorNum = (ushort)version.Minor,
                        Language = TwLanguage.ENGLISH,
                        Country = TwCountry.SOUTHAFRICA,
                        Info = asmName.Version.ToString()
                    },
                    ProtocolMajor = (ushort)(_isTwain2Enable ? 2 : 1),
                    ProtocolMinor = (ushort)(_isTwain2Enable ? 3 : 9),
                    SupportedGroups = TwDG.Image | TwDG.Control | (_isTwain2Enable ? TwDG.APP2 : 0),
                    Manufacturer =
                        ((AssemblyCompanyAttribute)asm.GetCustomAttributes(typeof(AssemblyCompanyAttribute), false)[0])
                        .Company,
                    ProductFamily = "TWAIN Class Library",
                    ProductName =
                        ((AssemblyProductAttribute)asm.GetCustomAttributes(typeof(AssemblyProductAttribute), false)[0])
                        .Product
                };
                return _appid;
            }
            set
            {
                if (value != null) throw new ArgumentException("Is read only property.");
                _appid = null;
            }
        }

        /// <summary>
        ///     Gets or sets the name of the application.
        ///     <para xml:lang="ru">Возвращает или устанавливает имя приложения.</para>
        /// </summary>
        [Category("Behavior")]
        [Description("Gets or sets the name of the application.")]
        public string AppProductName
        {
            get => AppId.ProductName;
            set => AppId.ProductName = value;
        }

        /// <summary>
        ///     Gets or sets a value indicating whether to display the UI of the TWAIN source.
        ///     <para xml:lang="ru">
        ///         Возвращает или устанавливает значение указывающие на необходимость отображения UI
        ///         TWAIN-источника.
        ///     </para>
        /// </summary>
        [Category("Behavior")]
        [DefaultValue(true)]
        [Description("Gets or sets a value indicating whether to display the UI of the TWAIN source.")]
        public bool ShowUi { get; set; }

        [Category("Behavior")]
        [DefaultValue(false)]
        private static bool ModalUi => false;

        /// <summary>
        ///     Gets or sets the parent window for the TWAIN source.
        ///     <para xml:lang="ru">Возвращает или устанавливает родительское окно для TWAIN-источника.</para>
        /// </summary>
        /// <value>
        ///     Окно.
        /// </value>
        [Category("Behavior")]
        [DefaultValue(false)]
        [Description(
            "Gets or sets the parent window for the TWAIN source.")]
        private static IWin32Window Parent => null;

        /// <summary>
        ///     Get or set the primary language for your application.
        ///     <para xml:lang="ru">Возвращает или устанавливает используемый приложением язык.</para>
        /// </summary>
        [Category("Culture")]
        [DefaultValue(TwLanguage.RUSSIAN)]
        [Description(
            "Get or set the primary language for your application.")]
        public TwLanguage Language
        {
            get => AppId.Version.Language;
            set => AppId.Version.Language = value;
        }

        /// <summary>
        ///     Get or set the primary country where your application is intended to be distributed.
        ///     <para xml:lang="ru">Возвращает или устанавливает страну происхождения приложения.</para>
        /// </summary>
        [Category("Culture")]
        [DefaultValue(TwCountry.BELARUS)]
        [Description(
            "Get or set the primary country where your application is intended to be distributed.")]
        public TwCountry Country
        {
            get => AppId.Version.Country;
            set => AppId.Version.Country = value;
        }

        /// <summary>
        ///     Gets or sets the frame of the physical location of the image.
        ///     <para xml:lang="ru">Возвращает или устанавливает кадр физического расположения изображения.</para>
        /// </summary>
        [Browsable(false)]
        [ReadOnly(true)]
        public RectangleF ImageLayout
        {
            get
            {
                var imageLayout = new TwImageLayout();
                var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Image, TwDAT.ImageLayout, TwMSG.Get, ref imageLayout);
                if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
                return imageLayout.Frame;
            }
            set
            {
                var imageLayout = new TwImageLayout { Frame = value };
                var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Image, TwDAT.ImageLayout, TwMSG.Set, ref imageLayout);
                if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
            }
        }

        /// <summary>
        ///     Returns a set of capabilities (Capabilities).
        ///     <para xml:lang="ru">Возвращает набор возможностей (Capabilities).</para>
        /// </summary>
        [Browsable(false)]
        [ReadOnly(true)]
        public TwainCapabilities Capabilities => _capabilities ?? (_capabilities = new TwainCapabilities(this));

        /// <summary>
        ///     Returns a set of operations for working with a color palette.
        ///     <para xml:lang="ru">Возвращает набор операций для работы с цветовой палитрой.</para>
        /// </summary>
        [Browsable(false)]
        [ReadOnly(true)]
        public TwainPalette Palette { get; }

        /// <summary>
        ///     Gets the permissions supported by the data source.
        ///     <para xml:lang="ru">Возвращает разрешения, поддерживаемые источником данных.</para>
        /// </summary>
        /// <returns>Collection of values.
        ///     <para xml:lang="ru">Коллекция значений.</para>
        /// </returns>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        [Obsolete("Use Twain32.Capabilities.XResolution.Get() instead.", true)]
        public Enumeration GetResolutions()
        {
            return Enumeration.FromObject(GetCap(TwCap.XResolution));
        }

        /// <summary>
        ///     Sets the current resolution.
        ///     <para xml:lang="ru">Устанавливает текущее разрешение.</para>
        /// </summary>
        /// <param name="value">Resolution.
        ///     <para xml:lang="ru">Разрешение.</para>
        /// </param>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        [Obsolete(
            "Use Twain32.Capabilities.XResolution.Set(value) and Twain32.Capabilities.YResolution.Set(value) instead.",
            true)]
        public void SetResolutions(float value)
        {
            SetCap(TwCap.XResolution, value);
            SetCap(TwCap.YResolution, value);
        }

        /// <summary>
        ///     Returns the pixel types supported by the data source.
        ///     <para xml:lang="ru">Возвращает типы пикселей, поддерживаемые источником данных.</para>
        /// </summary>
        /// <returns>Collection of values.
        ///     <para xml:lang="ru">Коллекция значений.</para>
        /// </returns>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        [Obsolete("Use Twain32.Capabilities.PixelType.Get() instead.", true)]
        public Enumeration GetPixelTypes()
        {
            var val = Enumeration.FromObject(GetCap(TwCap.IPixelType));
            for (var i = 0; i < val.Count; i++) val[i] = (TwPixelType)val[i];
            return val;
        }

        /// <summary>
        ///     Sets the current type of pixels.
        ///     <para xml:lang="ru">Устанавливает текущий тип пикселей.</para>
        /// </summary>
        /// <param name="value">Type of pixels.
        ///     <para xml:lang="ru">Тип пикселей.</para>
        /// </param>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        [Obsolete("Use Twain32.Capabilities.PixelType.Set(value) instead.", true)]
        public void SetPixelType(TwPixelType value)
        {
            SetCap(TwCap.IPixelType, value);
        }

        /// <summary>
        ///     Returns the units used by the data source.
        ///     <para xml:lang="ru">Возвращает единицы измерения, используемые источником данных.</para>
        /// </summary>
        /// <returns>Units.
        ///     <para xml:lang="ru">Единицы измерения.</para>
        /// </returns>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        [Obsolete("Use Twain32.Capabilities.Units.Get() instead.", true)]
        public Enumeration GetUnitOfMeasure()
        {
            var val = Enumeration.FromObject(GetCap(TwCap.IUnits));
            for (var i = 0; i < val.Count; i++) val[i] = (TwUnits)val[i];
            return val;
        }

        /// <summary>
        ///     Sets the current unit of measure used by the data source.
        ///     <para xml:lang="ru">Устанавливает текущую единицу измерения, используемую источником данных.</para>
        /// </summary>
        /// <param name="value">Unit of measurement.
        ///     <para xml:lang="ru">Единица измерения.</para>
        /// </param>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        [Obsolete("Use Twain32.Capabilities.Units.Set(value) instead.", true)]
        public void SetUnitOfMeasure(TwUnits value)
        {
            SetCap(TwCap.IUnits, value);
        }

        #endregion

        #region All capabilities

        /// <summary>
        ///     Returns flags indicating operations supported by the data source for the specified capability value.
        ///     <para xml:lang="ru">
        ///         Возвращает флаги, указывающие на поддерживаемые источником данных операции, для указанного
        ///         значения capability.
        ///     </para>
        /// </summary>
        /// <param name="capability">
        ///     The value of the TwCap enumeration.
        ///     <para xml:lang="ru">Значение перечисдения TwCap.</para>
        /// </param>
        /// <returns>
        ///     Set of flags.
        ///     <para xml:lang="ru">Набор флагов.</para>
        /// </returns>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        public TwQC IsCapSupported(TwCap capability)
        {
            if ((TwainState & TwainStateFlag.DsOpen) == 0)
                throw new TwainException("The data source is not open.");

            var cap = new TwCapability(capability);
            try
            {
                var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.Capability, TwMSG.QuerySupport, ref cap);
                if (rc == TwRC.Success) return (TwQC)((TwOneValue)cap.GetValue()).Item;
                return 0;
            }
            finally
            {
                cap.Dispose();
            }
        }

        /// <summary>
        ///     Returns the value for the specified capability.
        ///     <para xml:lang="ru">Возвращает значение для указанного capability (возможность).</para>
        /// </summary>
        /// <param name="capability">
        ///     The value of the TwCap enumeration.
        ///     <para xml:lang="ru">Значение перечисления TwCap.</para>
        /// </param>
        /// <param name="msg">
        ///     The value of the TwMSG enumeration.
        ///     <para xml:lang="ru">Значение перечисления TwMSG.</para>
        /// </param>
        /// <returns>
        ///     Depending on the value of capability, the following can be returned: type-value, array,
        ///     <see cref="Twain32.Range">range</see>, <see cref="Twain32.Enumeration">transfer</see>.
        ///     <para xml:lang="ru">
        ///         В зависимости от значение capability, могут быть возвращены: тип-значение, массив,
        ///         <see cref="Twain32.Range">диапазон</see>, <see cref="Twain32.Enumeration">перечисление</see>.
        ///     </para>
        /// </returns>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        private object GetCapCore(TwCap capability, TwMSG msg)
        {
            if ((TwainState & TwainStateFlag.DsOpen) != 0)
            {
                var cap = new TwCapability(capability);
                try
                {
                    var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.Capability, msg, ref cap);
                    if (rc == TwRC.Success)
                    {
                        switch (cap.ConType)
                        {
                            case TwOn.One:
                                var valueRaw = cap.GetValue();
                                return valueRaw is TwOneValue value
                                    ? TwTypeHelper.CastToCommon(value.ItemType,
                                        TwTypeHelper.ValueToTw(value.ItemType, value.Item))
                                    : valueRaw;

                            case TwOn.Range:
                                return Range.CreateRange((TwRange)cap.GetValue());

                            case TwOn.Array:
                                return ((__ITwArray)cap.GetValue()).Items;

                            case TwOn.Enum:
                                var enumeration = cap.GetValue() as __ITwEnumeration;

                                if (enumeration != null)
                                    return Enumeration.CreateEnumeration(enumeration.Items, enumeration.CurrentIndex,
                                        enumeration.DefaultIndex);
                                return null;
                        }

                        return cap.GetValue();
                    }
                    else
                    {
                        throw new TwainException(_GetTwainStatus(), rc);
                    }
                }
                finally
                {
                    cap.Dispose();
                }
            }

            throw new TwainException("The data source is not open.");
        }

        /// <summary>
        ///     Gets the values of the specified feature (capability).
        ///     <para xml:lang="ru">Возвращает значения указанной возможности (capability).</para>
        /// </summary>
        /// <param name="capability">
        ///     The value of the TwCap enumeration.
        ///     <para xml:lang="ru">Значение перечисления TwCap.</para>
        /// </param>
        /// <returns>
        ///     Depending on the value of capability, the following can be returned: type-value, array,
        ///     <see cref="Twain32.Range">range</see>, <see cref="Twain32.Enumeration">transfer</see>.
        ///     <para xml:lang="ru">
        ///         В зависимости от значение capability, могут быть возвращены: тип-значение, массив,
        ///         <see cref="Twain32.Range">диапазон</see>, <see cref="Twain32.Enumeration">перечисление</see>.
        ///     </para>
        /// </returns>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        public object GetCap(TwCap capability)
        {
            return GetCapCore(capability, TwMSG.Get);
        }

        /// <summary>
        ///     Returns the current value for the specified feature. (capability).
        ///     <para xml:lang="ru">Возвращает текущее значение для указанной возможности (capability).</para>
        /// </summary>
        /// <param name="capability">
        ///     The value of the TwCap enumeration.
        ///     <para xml:lang="ru">Значение перечисления TwCap.</para>
        /// </param>
        /// <returns>
        ///     Depending on the value of capability, the following can be returned: type-value, array,
        ///     <see cref="Twain32.Range">range</see>, <see cref="Twain32.Enumeration">transfer</see>.
        ///     <para xml:lang="ru">
        ///         В зависимости от значение capability, могут быть возвращены: тип-значение, массив,
        ///         <see cref="Twain32.Range">диапазон</see>, <see cref="Twain32.Enumeration">перечисление</see>.
        ///     </para>
        /// </returns>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        public object GetCurrentCap(TwCap capability)
        {
            return GetCapCore(capability, TwMSG.GetCurrent);
        }

        /// <summary>
        ///     Returns the default value for the specified feature. (capability).
        ///     <para xml:lang="ru">Возвращает значение по умолчанию для указанной возможности (capability).</para>
        /// </summary>
        /// <param name="capability">
        ///     The value of the TwCap enumeration.
        ///     <para xml:lang="ru">Значение перечисления TwCap.</para>
        /// </param>
        /// <returns>
        ///     Depending on the value of capability, the following can be returned: type-value, array,
        ///     <see cref="Twain32.Range">range</see>, <see cref="Twain32.Enumeration">transfer</see>.
        ///     <para xml:lang="ru">
        ///         В зависимости от значение capability, могут быть возвращены: тип-значение, массив,
        ///         <see cref="Twain32.Range">диапазон</see>, <see cref="Twain32.Enumeration">перечисление</see>.
        ///     </para>
        /// </returns>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        public object GetDefaultCap(TwCap capability)
        {
            return GetCapCore(capability, TwMSG.GetDefault);
        }

        /// <summary>
        ///     Resets the current value for the specified <see cref="TwCap">capability</see> to default value.
        ///     <para xml:lang="ru">
        ///         Сбрасывает текущее значение для указанного <see cref="TwCap">capability</see> в значение по
        ///         умолчанию.
        ///     </para>
        /// </summary>
        /// <param name="capability">
        ///     Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        public void ResetCap(TwCap capability)
        {
            if ((TwainState & TwainStateFlag.DsOpen) != 0)
            {
                var cap = new TwCapability(capability);
                try
                {
                    var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.Capability, TwMSG.Reset, ref cap);
                    if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
                }
                finally
                {
                    cap.Dispose();
                }
            }
            else
            {
                throw new TwainException("The data source is not open.");
            }
        }

        /// <summary>
        ///     Resets the current value of all current values to the default values.
        ///     <para xml:lang="ru">Сбрасывает текущее значение всех текущих значений в значения по умолчанию.</para>
        /// </summary>
        /// <exception cref="TwainException">Возбуждается в случае возникновения ошибки во время операции.</exception>
        public void ResetAllCap()
        {
            if ((TwainState & TwainStateFlag.DsOpen) != 0)
            {
                var cap = new TwCapability(TwCap.SupportedCaps);
                try
                {
                    var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.Capability, TwMSG.ResetAll, ref cap);
                    if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
                }
                finally
                {
                    cap.Dispose();
                }
            }
            else
            {
                throw new TwainException("The data source is not open.");
            }
        }

        private void _SetCapCore(TwCapability cap, TwMSG msg)
        {
            if ((TwainState & TwainStateFlag.DsOpen) != 0)
                try
                {
                    var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.Capability, msg, ref cap);
                    if (rc != TwRC.Success) throw new TwainException(_GetTwainStatus(), rc);
                }
                finally
                {
                    cap.Dispose();
                }
            else
                throw new TwainException("The data source is not open.");
        }

        private void _SetCapCore(TwCap capability, TwMSG msg, object value)
        {
            TwCapability cap;
            if (value is string)
            {
                var attrs = typeof(TwCap).GetField(capability.ToString())
                    ?.GetCustomAttributes(typeof(TwTypeAttribute), false);

                cap = attrs?.Length > 0
                    ? new TwCapability(capability, (string)value, ((TwTypeAttribute)attrs[0]).TwType)
                    : new TwCapability(capability, (string)value, TwTypeHelper.TypeOf(value));
            }
            else
            {
                var type = TwTypeHelper.TypeOf(value.GetType());
                cap = new TwCapability(capability, TwTypeHelper.ValueFromTw<uint>(TwTypeHelper.CastToTw(type, value)),
                    type);
            }

            _SetCapCore(cap, msg);
        }

        private void _SetCapCore(TwCap capability, TwMSG msg, object[] value)
        {
            var attrs = typeof(TwCap).GetField(capability.ToString())
                ?.GetCustomAttributes(typeof(TwTypeAttribute), false);
            _SetCapCore(
                new TwCapability(
                    capability,
                    new TwArray
                    {
                        ItemType = attrs?.Length > 0
                            ? ((TwTypeAttribute)attrs[0]).TwType
                            : TwTypeHelper.TypeOf(value[0]),
                        NumItems = (uint)value.Length
                    },
                    value),
                msg);
        }

        private void _SetCapCore(TwCap capability, TwMSG msg, Range value)
        {
            _SetCapCore(new TwCapability(capability, value.ToTwRange()), msg);
        }

        private void _SetCapCore(TwCap capability, TwMSG msg, Enumeration value)
        {
            var attrs = typeof(TwCap).GetField(capability.ToString())
                ?.GetCustomAttributes(typeof(TwTypeAttribute), false);
            _SetCapCore(
                new TwCapability(
                    capability,
                    new TwEnumeration
                    {
                        ItemType = attrs?.Length > 0
                            ? ((TwTypeAttribute)attrs[0]).TwType
                            : TwTypeHelper.TypeOf(value[0]),
                        NumItems = (uint)value.Count,
                        CurrentIndex = (uint)value.CurrentIndex,
                        DefaultIndex = (uint)value.DefaultIndex
                    },
                    value.Items),
                msg);
        }

        /// <summary>
        ///     Sets the value for the specified <see cref="TwCap">capability</see>
        ///     <para xml:lang="ru">Устанавливает значение для указанного <see cref="TwCap">capability</see></para>
        /// </summary>
        /// <param name="capability">Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <param name="value">The value to set.
        ///     <para xml:lang="ru">Устанавливаемое значение.</para>
        /// </param>
        /// <exception cref="TwainException">Возникает в случае, если источник данных не открыт.</exception>
        public void SetCap(TwCap capability, object value)
        {
            _SetCapCore(capability, TwMSG.Set, value);
        }

        /// <summary>
        ///     Sets the value for the specified <see cref="TwCap">capability</see>
        ///     <para xml:lang="ru">Устанавливает значение для указанного <see cref="TwCap">capability</see></para>
        /// </summary>
        /// <param name="capability">Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <param name="value">The value to set.
        ///     <para xml:lang="ru">Устанавливаемое значение.</para>
        /// </param>
        /// <exception cref="TwainException">Возникает в случае, если источник данных не открыт.</exception>
        public void SetCap(TwCap capability, object[] value)
        {
            _SetCapCore(capability, TwMSG.Set, value);
        }

        /// <summary>
        ///     Sets the value for the specified <see cref="TwCap">capability</see>
        ///     <para xml:lang="ru">Устанавливает значение для указанного <see cref="TwCap">capability</see></para>
        /// </summary>
        /// <param name="capability">Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <param name="value">The value to set.
        ///     <para xml:lang="ru">Устанавливаемое значение.</para>
        /// </param>
        /// <exception cref="TwainException">Возникает в случае, если источник данных не открыт.</exception>
        public void SetCap(TwCap capability, Range value)
        {
            _SetCapCore(capability, TwMSG.Set, value);
        }

        /// <summary>
        ///     Sets the value for the specified <see cref="TwCap">capability</see>
        ///     <para xml:lang="ru">Устанавливает значение для указанного <see cref="TwCap">capability</see></para>
        /// </summary>
        /// <param name="capability">Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <param name="value">The value to set.
        ///     <para xml:lang="ru">Устанавливаемое значение.</para>
        /// </param>
        /// <exception cref="TwainException">Возникает в случае, если источник данных не открыт.</exception>
        public void SetCap(TwCap capability, Enumeration value)
        {
            _SetCapCore(capability, TwMSG.Set, value);
        }

        /// <summary>
        ///     Sets a limit on the values of the specified feature.
        ///     <para xml:lang="ru">Устанавливает ограничение на значения указанной возможности.</para>
        /// </summary>
        /// <param name="capability">Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <param name="value">The value to set.
        ///     <para xml:lang="ru">Устанавливаемое значение.</para>
        /// </param>
        /// <exception cref="TwainException">Возникает в случае, если источник данных не открыт.</exception>
        public void SetConstraintCap(TwCap capability, object value)
        {
            _SetCapCore(capability, TwMSG.SetConstraint, value);
        }

        /// <summary>
        ///     Sets a limit on the values of the specified feature.
        ///     <para xml:lang="ru">Устанавливает ограничение на значения указанной возможности.</para>
        /// </summary>
        /// <param name="capability">Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <param name="value">The value to set.
        ///     <para xml:lang="ru">Устанавливаемое значение.</para>
        /// </param>
        /// <exception cref="TwainException">Возникает в случае, если источник данных не открыт.</exception>
        public void SetConstraintCap(TwCap capability, object[] value)
        {
            _SetCapCore(capability, TwMSG.SetConstraint, value);
        }

        /// <summary>
        ///     Sets a limit on the values of the specified feature.
        ///     <para xml:lang="ru">Устанавливает ограничение на значения указанной возможности.</para>
        /// </summary>
        /// <param name="capability">Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <param name="value">The value to set.
        ///     <para xml:lang="ru">Устанавливаемое значение.</para>
        /// </param>
        /// <exception cref="TwainException">Возникает в случае, если источник данных не открыт.</exception>
        public void SetConstraintCap(TwCap capability, Range value)
        {
            _SetCapCore(capability, TwMSG.SetConstraint, value);
        }

        /// <summary>
        ///     Sets a limit on the values of the specified feature.
        ///     <para xml:lang="ru">Устанавливает ограничение на значения указанной возможности.</para>
        /// </summary>
        /// <param name="capability">Listing Value <see cref="TwCap" />.
        ///     <para xml:lang="ru">Значение перечисления <see cref="TwCap" />.</para>
        /// </param>
        /// <param name="value">The value to set.
        ///     <para xml:lang="ru">Устанавливаемое значение.</para>
        /// </param>
        /// <exception cref="TwainException">Возникает в случае, если источник данных не открыт.</exception>
        public void SetConstraintCap(TwCap capability, Enumeration value)
        {
            _SetCapCore(capability, TwMSG.SetConstraint, value);
        }

        #endregion

        #region DG_IMAGE / IMAGExxxxXFER / MSG_GET operation

        /// <summary>
        ///     Performs image transfer (Native Mode Transfer).
        ///     <para xml:lang="ru">Выполняет передачу изображения (Native Mode Transfer).</para>
        /// </summary>
        private void _NativeTransferPictures()
        {
            if (_srcds.Id == 0) return;

            var pxfr = new TwPendingXfers();
            try
            {
                _images.Clear();

                do
                {
                    pxfr.Count = 0;
                    var hBitmap = IntPtr.Zero;

                    for (var rc = _dsmEntry.DsImageXfer(AppId, _srcds, TwDG.Image, TwDAT.ImageNativeXfer, TwMSG.Get,
                            ref hBitmap);
                        rc != TwRC.XferDone;) throw new TwainException(_GetTwainStatus(), rc);
                    // DG_IMAGE / DAT_IMAGEINFO / MSG_GET
                    // DG_IMAGE / DAT_EXTIMAGEINFO / MSG_GET
                    if (_OnXferDone(new XferDoneEventArgs(_GetImageInfo, _GetExtImageInfo))) return;

                    var pBitmap = _Memory.Lock(hBitmap);
                    try
                    {
                        SealedImage img;

                        var handler = GetService(typeof(IImageHandler)) as IImageHandler;
                        if (handler == null)
                            switch (Environment.OSVersion.Platform)
                            {
                                case PlatformID.Unix:
                                    handler = new Tiff();
                                    break;
                                case PlatformID.MacOSX:
                                    handler = new Pict();
                                    break;
                                default:
                                    handler = new DibToImage();
                                    break;
                            }

                        img = handler.PtrToStream(pBitmap, GetService(typeof(IStreamProvider)) as IStreamProvider);

                        _images.Add(img);
                        if (_OnEndXfer(new EndXferEventArgs(img))) return;
                    }
                    finally
                    {
                        _Memory.Unlock(hBitmap);
                        _Memory.Free(hBitmap);
                    }

                    for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.PendingXfers, TwMSG.EndXfer,
                            ref pxfr);
                        rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
                } while (pxfr.Count != 0);
            }
            finally
            {
                _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.PendingXfers, TwMSG.Reset, ref pxfr);
            }
        }

        /// <summary>
        ///     Performs image transfer (Disk File Mode Transfer).
        ///     <para xml:lang="ru">Выполняет передачу изображения (Disk File Mode Transfer).</para>
        /// </summary>
        private void _FileTransferPictures()
        {
            if (_srcds.Id == 0) return;

            var pxfr = new TwPendingXfers();
            try
            {
                _images.Clear();
                do
                {
                    pxfr.Count = 0;

                    var args = new SetupFileXferEventArgs();
                    if (_OnSetupFileXfer(args)) return;

                    var fileXfer = new TwSetupFileXfer
                    {
                        Format = Capabilities.ImageFileFormat.IsSupported(TwQC.GetCurrent)
                            ? Capabilities.ImageFileFormat.GetCurrent()
                            : TwFF.Bmp,
                        FileName = string.IsNullOrEmpty(args.FileName) ? Path.GetTempFileName() : args.FileName
                    };

                    for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.SetupFileXfer, TwMSG.Set,
                            ref fileXfer);
                        rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);

                    for (var rc = _dsmEntry.DsRaw(AppId, _srcds, TwDG.Image, TwDAT.ImageFileXfer, TwMSG.Get,
                            IntPtr.Zero);
                        rc != TwRC.XferDone;) throw new TwainException(_GetTwainStatus(), rc);
                    // DG_IMAGE / DAT_IMAGEINFO / MSG_GET
                    // DG_IMAGE / DAT_EXTIMAGEINFO / MSG_GET
                    if (_OnXferDone(new XferDoneEventArgs(_GetImageInfo, _GetExtImageInfo))) return;

                    for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.PendingXfers, TwMSG.EndXfer,
                            ref pxfr);
                        rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
                    for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.SetupFileXfer, TwMSG.Get,
                            ref fileXfer);
                        rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
                    if (_OnFileXfer(new FileXferEventArgs(ImageFileXfer.Create(fileXfer)))) return;
                } while (pxfr.Count != 0);
            }
            finally
            {
                _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.PendingXfers, TwMSG.Reset, ref pxfr);
            }
        }

        /// <summary>
        ///     Performs image transfer (Buffered Memory Mode Transfer and Memory File Mode Transfer).
        ///     <para xml:lang="ru">Выполняет передачу изображения (Buffered Memory Mode Transfer and Memory File Mode Transfer).</para>
        /// </summary>
        private void _MemoryTransferPictures(bool isMemFile)
        {
            if (_srcds.Id == 0) return;

            var pxfr = new TwPendingXfers();
            try
            {
                _images.Clear();
                do
                {
                    pxfr.Count = 0;
                    var info = _GetImageInfo();

                    if (isMemFile)
                        if ((Capabilities.ImageFileFormat.IsSupported() & TwQC.GetCurrent) != 0)
                        {
                            var fileXfer = new TwSetupFileXfer
                            {
                                Format = Capabilities.ImageFileFormat.GetCurrent()
                            };
                            for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.SetupFileXfer,
                                    TwMSG.Set, ref fileXfer);
                                rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
                        }

                    var memBufSize = new TwSetupMemXfer();

                    for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.SetupMemXfer, TwMSG.Get,
                            ref memBufSize);
                        rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
                    if (_OnSetupMemXfer(new SetupMemXferEventArgs(info, memBufSize.Preferred))) return;

                    var hMem = _Memory.Alloc((int)memBufSize.Preferred);
                    if (hMem == IntPtr.Zero) throw new TwainException("Error allocating memory.");
                    try
                    {
                        var mem = new TwMemory
                        {
                            Flags = TwMF.AppOwns | TwMF.Pointer,
                            Length = memBufSize.Preferred,
                            TheMem = _Memory.Lock(hMem)
                        };

                        do
                        {
                            var memXferBuf = new TwImageMemXfer { Memory = mem };
                            _Memory.ZeroMemory(memXferBuf.Memory.TheMem, (IntPtr)memXferBuf.Memory.Length);

                            var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Image,
                                isMemFile ? TwDAT.ImageMemFileXfer : TwDAT.ImageMemXfer, TwMSG.Get, ref memXferBuf);
                            if (rc != TwRC.Success && rc != TwRC.XferDone)
                            {
                                var cc = _GetTwainStatus();
                                _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.PendingXfers, TwMSG.EndXfer,
                                    ref pxfr);
                                throw new TwainException(cc, rc);
                            }

                            if (_OnMemXfer(new MemXferEventArgs(info, ImageMemXfer.Create(memXferBuf)))) return;

                            if (rc != TwRC.XferDone)
                                continue;
                            // DG_IMAGE / DAT_IMAGEINFO / MSG_GET
                            // DG_IMAGE / DAT_EXTIMAGEINFO / MSG_GET
                            if (_OnXferDone(new XferDoneEventArgs(_GetImageInfo, _GetExtImageInfo))) return;
                            break;
                        } while (true);
                    }
                    finally
                    {
                        _Memory.Unlock(hMem);
                        _Memory.Free(hMem);
                    }

                    for (var rc = _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.PendingXfers, TwMSG.EndXfer,
                            ref pxfr);
                        rc != TwRC.Success;) throw new TwainException(_GetTwainStatus(), rc);
                } while (pxfr.Count != 0);
            }
            finally
            {
                _dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.PendingXfers, TwMSG.Reset, ref pxfr);
            }
        }

        #endregion

        #region DS events handler

        /// <summary>
        ///     A data source event handler.
        ///     <para xml:lang="ru">Обработчик событий источника данных.</para>
        /// </summary>
        /// <param name="appId">Description of the application.
        ///     <para xml:lang="ru">Описание приложения.</para>
        /// </param>
        /// <param name="srcId">Description of the data source.
        ///     <para xml:lang="ru">Описание источника данных.</para>
        /// </param>
        /// <param name="dg">Description of the data group.
        ///     <para xml:lang="ru">Описание группы данных.</para>
        /// </param>
        /// <param name="dat">Description of the data.
        ///     <para xml:lang="ru">Описание данных.</para>
        /// </param>
        /// <param name="msg">Message.
        ///     <para xml:lang="ru">Сообщение.</para>
        /// </param>
        /// <param name="data">Data.
        ///     <para xml:lang="ru">Данные.</para>
        /// </param>
        /// <returns>Result event handlers.
        ///     <para xml:lang="ru">Результат обработники события.</para>
        /// </returns>
        private TwRC _TwCallbackProc(TwIdentity srcId, TwIdentity appId, TwDG dg, TwDAT dat, TwMSG msg, IntPtr data)
        {
            try
            {
                if (appId == null || appId.Id != AppId.Id) return TwRC.Failure;

                if ((TwainState & TwainStateFlag.DsEnabled) == 0)
                    TwainState |= TwainStateFlag.DsEnabled | TwainStateFlag.DsReady;

                _TwCallbackProcCore(msg, isCloseReq =>
                {
                    if (isCloseReq || DisableAfterAcquire) DisableDataSource();
                });
            }
            catch (Exception ex)
            {
                _OnAcquireError(new AcquireErrorEventArgs(new TwainException(ex.Message, ex)));
            }

            return TwRC.Success;
        }

        /// <summary>
        ///     An internal data source event handler.
        ///     <para xml:lang="ru">Внутренний обработчик событий источника данных.</para>
        /// </summary>
        /// <param name="msg">Message.
        ///     <para xml:lang="ru">Сообщение.</para>
        /// </param>
        /// <param name="endAction">The action that completes the processing of the event.
        ///     <para xml:lang="ru">Действие, завершающее обработку события.</para>
        /// </param>
        private void _TwCallbackProcCore(TwMSG msg, Action<bool> endAction)
        {
            try
            {
                switch (msg)
                {
                    case TwMSG.XFerReady:
                        switch (Capabilities.XferMech.GetCurrent())
                        {
                            case TwSX.File:
                                _FileTransferPictures();
                                break;
                            case TwSX.Memory:
                                _MemoryTransferPictures(false);
                                break;
                            case TwSX.MemFile:
                                _MemoryTransferPictures(true);
                                break;
                            default:
                                _NativeTransferPictures();
                                break;
                        }

                        endAction(false);
                        _OnAcquireCompleted(new EventArgs());
                        break;
                    case TwMSG.CloseDSReq:
                        endAction(true);
                        break;
                    case TwMSG.CloseDSOK:
                        endAction(false);
                        break;
                    case TwMSG.DeviceEvent:
                        _DeviceEventObtain();
                        break;
                }
            }
            catch (TwainException ex)
            {
                try
                {
                    endAction(false);
                }
                catch
                {
                    // ignored
                }

                _OnAcquireError(new AcquireErrorEventArgs(ex));
            }
            catch
            {
                try
                {
                    endAction(false);
                }
                catch
                {
                    // ignored
                }

                throw;
            }
        }

        private void _DeviceEventObtain()
        {
            var deviceEvent = new TwDeviceEvent();
            if (_dsmEntry.DsInvoke(AppId, _srcds, TwDG.Control, TwDAT.DeviceEvent, TwMSG.Get, ref deviceEvent) ==
                TwRC.Success) _OnDeviceEvent(new DeviceEventEventArgs(deviceEvent));
        }

        #endregion

        #region Raise events

        private void _OnAcquireCompleted(EventArgs e)
        {
            AcquireCompleted?.Invoke(this, e);
        }

        private void _OnAcquireError(AcquireErrorEventArgs e)
        {
            AcquireError?.Invoke(this, e);
        }

        private bool _OnXferDone(XferDoneEventArgs e)
        {
            XferDone?.Invoke(this, e);
            return e.Cancel;
        }

        private bool _OnEndXfer(EndXferEventArgs e)
        {
            EndXfer?.Invoke(this, e);
            return e.Cancel;
        }

        private bool _OnSetupMemXfer(SetupMemXferEventArgs e)
        {
            SetupMemXferEvent?.Invoke(this, e);
            return e.Cancel;
        }

        private bool _OnMemXfer(MemXferEventArgs e)
        {
            MemXferEvent?.Invoke(this, e);
            return e.Cancel;
        }

        private bool _OnSetupFileXfer(SetupFileXferEventArgs e)
        {
            SetupFileXferEvent?.Invoke(this, e);
            return e.Cancel;
        }

        private bool _OnFileXfer(FileXferEventArgs e)
        {
            FileXferEvent?.Invoke(this, e);
            return e.Cancel;
        }

        private void _OnDeviceEvent(DeviceEventEventArgs e)
        {
            DeviceEvent?.Invoke(this, e);
        }

        #endregion

        #region Events

        /// <summary>
        ///     Occurs when the acquire is completed.
        ///     <para xml:lang="ru">Возникает в момент окончания сканирования.</para>
        /// </summary>
        [Category("Action")]
        [Description("Occurs when the acquire is completed.")]
        public event EventHandler AcquireCompleted;

        /// <summary>
        ///     Occurs when error received during acquire.
        ///     <para xml:lang="ru">Возникает в момент получения ошибки в процессе сканирования.</para>
        /// </summary>
        [Category("Action")]
        [Description(
            "Occurs when error received during acquire.")]
        public event EventHandler<AcquireErrorEventArgs> AcquireError;

        /// <summary>
        ///     Occurs when the transfer into application was completed (Native Mode Transfer).
        ///     <para xml:lang="ru">Возникает в момент окончания получения изображения приложением.</para>
        /// </summary>
        [Category("Native Mode Action")]
        [Description(
            "Occurs when the transfer into application was completed (Native Mode Transfer).")]
        public event EventHandler<EndXferEventArgs> EndXfer;

        /// <summary>
        ///     Occurs when the transfer was completed.
        ///     <para xml:lang="ru">Возникает в момент окончания получения изображения источником.</para>
        /// </summary>
        [Category("Action")]
        [Description(
            "Occurs when the transfer was completed.")]
        public event EventHandler<XferDoneEventArgs> XferDone;

        /// <summary>
        ///     Occurs when determined size of buffer to use during the transfer (Memory Mode Transfer and MemFile Mode Transfer).
        ///     <para xml:lang="ru">Возникает в момент установки размера буфера памяти.</para>
        /// </summary>
        [Category("Memory Mode Action")]
        [Description(
            "Occurs when determined size of buffer to use during the transfer (Memory Mode Transfer and MemFile Mode Transfer).")]
        public event EventHandler<SetupMemXferEventArgs> SetupMemXferEvent;

        /// <summary>
        ///     Occurs when the memory block for the data was recived (Memory Mode Transfer and MemFile Mode Transfer).
        ///     <para xml:lang="ru">Возникает в момент получения очередного блока данных.</para>
        /// </summary>
        [Category("Memory Mode Action")]
        [Description(
            "Occurs when the memory block for the data was recived (Memory Mode Transfer and MemFile Mode Transfer).")]
        public event EventHandler<MemXferEventArgs> MemXferEvent;

        /// <summary>
        ///     Occurs when you need to specify the filename (File Mode Transfer).
        ///     <para xml:lang="ru">Возникает в момент, когда необходимо задать имя файла изображения.</para>
        /// </summary>
        [Category("File Mode Action")]
        [Description(
            "Occurs when you need to specify the filename. (File Mode Transfer)")]
        public event EventHandler<SetupFileXferEventArgs> SetupFileXferEvent;

        /// <summary>
        ///     Occurs when the transfer into application was completed (File Mode Transfer).
        ///     <para xml:lang="ru">Возникает в момент окончания получения файла изображения приложением.</para>
        /// </summary>
        [Category("File Mode Action")]
        [Description(
            "Occurs when the transfer into application was completed (File Mode Transfer).")]
        public event EventHandler<FileXferEventArgs> FileXferEvent;

        /// <summary>
        ///     Occurs when TWAIN state was changed.
        ///     <para xml:lang="ru">Возникает в момент изменения состояния twain-устройства.</para>
        /// </summary>
        [Category("Behavior")]
        [Description("Occurs when TWAIN state was changed.")]
        public event EventHandler<TwainStateEventArgs> TwainStateChanged;

        /// <summary>
        ///     Occurs when enabled the source sends this message to the Application to alert it that some event has taken place.
        ///     <para xml:lang="ru">Возникает в момент, когда источник уведомляет приложение о произошедшем событии.</para>
        /// </summary>
        [Category("Behavior")]
        [Description(
            "Occurs when enabled the source sends this message to the Application to alert it that some event has taken place.")]
        public event EventHandler<DeviceEventEventArgs> DeviceEvent;

        #endregion

        #region Events Args

        /// <summary>
        ///     Arguments for the EndXfer event.
        ///     <para xml:lang="ru">Аргументы события EndXfer.</para>
        /// </summary>
        [Serializable]
        public sealed class EndXferEventArgs : SerializableCancelEventArgs
        {
            private SealedImage _image;

            /// <summary>
            ///     Initializes a new instance of the class.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса.</para>
            /// </summary>
            /// <param name="image">Image.
            ///     <para xml:lang="ru">Изображение.</para>
            /// </param>
            internal EndXferEventArgs(object image)
            {
                _image = image as SealedImage;
            }

            public T CreateImage<T>(IImageFactory<T> factory) where T : class
            {
                return factory.Create(_image);
            }

            /// <summary>
            ///     Returns the image.
            ///     <para xml:lang="ru">Возвращает изображение.</para>
            /// </summary>
            public Image Image => _image;

#if !NET2
            /// <summary>
            /// Returns the image.
            /// <para xml:lang="ru">Возвращает изображение.</para>
            /// </summary>
            public System.Windows.Media.ImageSource ImageSource {
                get {
                    return this._image;
                }
            }
#endif
        }

        /// <summary>
        ///     Arguments for the XferDone event.
        ///     <para xml:lang="ru">Аргументы события XferDone.</para>
        /// </summary>
        public sealed class XferDoneEventArgs : SerializableCancelEventArgs
        {
            private readonly GetExtImageInfoCallback _extImageInfoMethod;
            private readonly GetImageInfoCallback _imageInfoMethod;

            /// <summary>
            ///     Initializes a new instance of the class <see cref="XferDoneEventArgs" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса <see cref="XferDoneEventArgs" />.</para>
            /// </summary>
            /// <param name="method1">Callback method to get image description.
            ///     <para xml:lang="ru">Метод обратного вызова для получения описания изображения.</para>
            /// </param>
            /// <param name="method2">Callback method to get an extended image description.
            ///     <para xml:lang="ru">Метод обратного вызова для получения расширенного описания изображения.</para>
            /// </param>
            internal XferDoneEventArgs(GetImageInfoCallback method1, GetExtImageInfoCallback method2)
            {
                _imageInfoMethod = method1;
                _extImageInfoMethod = method2;
            }

            /// <summary>
            ///     Returns a description of the received image.
            ///     <para xml:lang="ru">Возвращает описание полученного изображения.</para>
            /// </summary>
            /// <returns>Description of the image.
            ///     <para xml:lang="ru">Описание изображения.</para>
            /// </returns>
            public ImageInfo GetImageInfo()
            {
                return _imageInfoMethod();
            }

            /// <summary>
            ///     Returns an extended description of the resulting image.
            ///     <para xml:lang="ru">Возвращает расширенного описание полученного изображения.</para>
            /// </summary>
            /// <param name="extInfo">A set of codes for the extended image description for which you want to get a description.
            ///     <para xml:lang="ru">Набор кодов расширенного описания изображения для которых требуется получить описание.</para>
            /// </param>
            /// <returns>Extended image description.
            ///     <para xml:lang="ru">Расширенное описание изображения.</para>
            /// </returns>
            public ExtImageInfo GetExtImageInfo(params TwEI[] extInfo)
            {
                return _extImageInfoMethod(extInfo);
            }
        }

        /// <summary>
        ///     Arguments for the SetupMemXferEvent event.
        ///     <para xml:lang="ru">Аргументы события SetupMemXferEvent.</para>
        /// </summary>
        [Serializable]
        public sealed class SetupMemXferEventArgs : SerializableCancelEventArgs
        {
            /// <summary>
            ///     Initializes a new instance of the class <see cref="SetupMemXferEventArgs" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса <see cref="SetupMemXferEventArgs" />.</para>
            /// </summary>
            /// <param name="info">Description of the image.
            ///     <para xml:lang="ru">Описание изображения.</para>
            /// </param>
            /// <param name="bufferSize">The size of the memory buffer for data transfer.
            ///     <para xml:lang="ru">Размер буфера памяти для передачи данных.</para>
            /// </param>
            internal SetupMemXferEventArgs(ImageInfo info, uint bufferSize)
            {
                ImageInfo = info;
                BufferSize = bufferSize;
            }

            /// <summary>
            ///     Returns a description of the image.
            ///     <para xml:lang="ru">Возвращает описание изображения.</para>
            /// </summary>
            public ImageInfo ImageInfo { get; private set; }

            /// <summary>
            ///     Gets the size of the memory buffer for data transfer.
            ///     <para xml:lang="ru">Возвращает размер буфера памяти для передачи данных.</para>
            /// </summary>
            public uint BufferSize { get; private set; }
        }

        /// <summary>
        ///     Arguments for the MemXferEvent event.
        ///     <para xml:lang="ru">Аргументы события MemXferEvent.</para>
        /// </summary>
        [Serializable]
        public sealed class MemXferEventArgs : SerializableCancelEventArgs
        {
            /// <summary>
            ///     Initializes a new instance of the class <see cref="MemXferEventArgs" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса <see cref="MemXferEventArgs" />.</para>
            /// </summary>
            /// <param name="info">Description of the image.
            ///     <para xml:lang="ru">Описание изображения.</para>
            /// </param>
            /// <param name="image">A fragment of image data.
            ///     <para xml:lang="ru">Фрагмент данных изображения.</para>
            /// </param>
            internal MemXferEventArgs(ImageInfo info, ImageMemXfer image)
            {
                ImageInfo = info;
                ImageMemXfer = image;
            }

            /// <summary>
            ///     Returns a description of the image.
            ///     <para xml:lang="ru">Возвращает описание изображения.</para>
            /// </summary>
            public ImageInfo ImageInfo { get; private set; }

            /// <summary>
            ///     Returns a piece of image data.
            ///     <para xml:lang="ru">Возвращает фрагмент данных изображения.</para>
            /// </summary>
            public ImageMemXfer ImageMemXfer { get; private set; }
        }

        /// <summary>
        ///     Arguments for the SetupFileXferEvent event.
        ///     <para xml:lang="ru">Аргументы события SetupFileXferEvent.</para>
        /// </summary>
        [Serializable]
        public sealed class SetupFileXferEventArgs : SerializableCancelEventArgs
        {
            /// <summary>
            ///     Initializes a new instance of the class <see cref="SetupFileXferEventArgs" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса <see cref="SetupFileXferEventArgs" />.</para>
            /// </summary>
            internal SetupFileXferEventArgs()
            {
            }

            /// <summary>
            ///     Gets or sets the name of the image file.
            ///     <para xml:lang="ru">Возвращает или устанавливает имя файла изображения.</para>
            /// </summary>
            public string FileName { get; set; }
        }

        /// <summary>
        ///     Arguments for the FileXferEvent event.
        ///     <para xml:lang="ru">Аргументы события FileXferEvent.</para>
        /// </summary>
        [Serializable]
        public sealed class FileXferEventArgs : SerializableCancelEventArgs
        {
            /// <summary>
            ///     Initializes a new instance of the class <see cref="FileXferEventArgs" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса <see cref="FileXferEventArgs" />.</para>
            /// </summary>
            /// <param name="image">Description of the image file.
            ///     <para xml:lang="ru">Описание файла изображения.</para>
            /// </param>
            internal FileXferEventArgs(ImageFileXfer image)
            {
                ImageFileXfer = image;
            }

            /// <summary>
            ///     Returns a description of the image file.
            ///     <para xml:lang="ru">Возвращает описание файла изображения.</para>
            /// </summary>
            public ImageFileXfer ImageFileXfer { get; private set; }
        }

        /// <summary>
        ///     Arguments for the TwainStateChanged event.
        ///     <para xml:lang="ru">Аргументы события TwainStateChanged.</para>
        /// </summary>
        [Serializable]
        public sealed class TwainStateEventArgs : EventArgs
        {
            /// <summary>
            ///     Initializes a new instance of the class.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса.</para>
            /// </summary>
            /// <param name="flags">State flags.
            ///     <para xml:lang="ru">Флаги состояния.</para>
            /// </param>
            internal TwainStateEventArgs(TwainStateFlag flags)
            {
                TwainState = flags;
            }

            /// <summary>
            ///     Returns the status flags of a twain device.
            ///     <para xml:lang="ru">Возвращает флаги состояния twain-устройства.</para>
            /// </summary>
            public TwainStateFlag TwainState { get; private set; }
        }

        /// <summary>
        ///     Arguments for the DeviceEvent event.
        ///     <para xml:lang="ru">Аргументы события DeviceEvent.</para>
        /// </summary>
        public sealed class DeviceEventEventArgs : EventArgs
        {
            private readonly TwDeviceEvent _deviceEvent;

            internal DeviceEventEventArgs(TwDeviceEvent deviceEvent)
            {
                _deviceEvent = deviceEvent;
            }

            /// <summary>
            ///     One of the TWDE_xxxx values.
            /// </summary>
            public TwDE Event => _deviceEvent.Event;

            /// <summary>
            ///     The name of the device that generated the event.
            /// </summary>
            public string DeviceName => _deviceEvent.DeviceName;

            /// <summary>
            ///     Battery Minutes Remaining.
            /// </summary>
            public uint BatteryMinutes => _deviceEvent.BatteryMinutes;

            /// <summary>
            ///     Battery Percentage Remaining.
            /// </summary>
            public short BatteryPercentAge => _deviceEvent.BatteryPercentAge;

            /// <summary>
            ///     Power Supply.
            /// </summary>
            public int PowerSupply => _deviceEvent.PowerSupply;

            /// <summary>
            ///     Resolution.
            /// </summary>
            public float XResolution => _deviceEvent.XResolution;

            /// <summary>
            ///     Resolution.
            /// </summary>
            public float YResolution => _deviceEvent.YResolution;

            /// <summary>
            ///     Flash Used2.
            /// </summary>
            public uint FlashUsed2 => _deviceEvent.FlashUsed2;

            /// <summary>
            ///     Automatic Capture.
            /// </summary>
            public uint AutomaticCapture => _deviceEvent.AutomaticCapture;

            /// <summary>
            ///     Automatic Capture.
            /// </summary>
            public uint TimeBeforeFirstCapture => _deviceEvent.TimeBeforeFirstCapture;

            /// <summary>
            ///     Automatic Capture.
            /// </summary>
            public uint TimeBetweenCaptures => _deviceEvent.TimeBetweenCaptures;
        }

        /// <summary>
        ///     Arguments for the AcquireError event.
        ///     <para xml:lang="ru">Аргументы события AcquireError.</para>
        /// </summary>
        [Serializable]
        public sealed class AcquireErrorEventArgs : EventArgs
        {
            /// <summary>
            ///     Initializes a new instance of the class.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса.</para>
            /// </summary>
            /// <param name="ex">An instance of the exception class.
            ///     <para xml:lang="ru">Экземпляр класса исключения.</para>
            /// </param>
            internal AcquireErrorEventArgs(TwainException ex)
            {
                Exception = ex;
            }

            /// <summary>
            ///     Gets an instance of the exception class.
            ///     <para xml:lang="ru">Возвращает экземпляр класса исключения.</para>
            /// </summary>
            public TwainException Exception { get; private set; }
        }

        /// <summary>
        /// </summary>
        /// <seealso cref="System.EventArgs" />
        [Serializable]
        public class SerializableCancelEventArgs : EventArgs
        {
            /// <summary>
            ///     Gets or sets a value indicating whether the event should be canceled.
            ///     <para xml:lang="ru">Получает или задает значение, показывающее, следует ли отменить событие.</para>
            /// </summary>
            /// <value>
            ///     Значение <c>true</c>, если событие следует отменить, в противном случае — значение <c>false</c>.
            /// </value>
            public bool Cancel { get; set; }
        }

        #endregion

        #region Nested classes

        /// <summary>
        ///     Entry points for working with DSM.
        ///     <para xml:lang="ru">Точки входа для работы с DSM.</para>
        /// </summary>
        private sealed class DsmEntry
        {
            /// <summary>
            ///     Initializes a new instance of the class <see cref="DsmEntry" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса <see cref="DsmEntry" />.</para>
            /// </summary>
            /// <param name="ptr">Pointer to DSM_Entry.
            ///     <para xml:lang="ru">Указатель на DSM_Entry.</para>
            /// </param>
            private DsmEntry(IntPtr ptr)
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                        DsmParent = _LinuxDsmParent;
                        DsmRaw = _LinuxDsmRaw;
                        DsImageXfer = _LinuxDsImageXfer;
                        DsRaw = _LinuxDsRaw;
                        break;
                    case PlatformID.MacOSX:
                        DsmParent = _MacosxDsmParent;
                        DsmRaw = _MacosxDsmRaw;
                        DsImageXfer = _MacosxDsImageXfer;
                        DsRaw = _MacosxDsRaw;
                        break;
                    default:
                        var createDelegate = typeof(DsmEntry).GetMethod("CreateDelegate",
                            BindingFlags.Static | BindingFlags.NonPublic);
                        foreach (var prop in typeof(DsmEntry).GetProperties())
                            if (createDelegate != null)
                                prop.SetValue(this,
                                    createDelegate.MakeGenericMethod(prop.PropertyType)
                                        .Invoke(this, new object[] { ptr }), null);
                        break;
                }
            }

            /// <summary>
            ///     Creates and returns a new instance of the class <see cref="DsmEntry" />.
            ///     <para xml:lang="ru">Создает и возвращает новый экземпляр класса <see cref="DsmEntry" />.</para>
            /// </summary>
            /// <param name="ptr">Pointer to DSM_Entry.
            ///     <para xml:lang="ru">Указатель на DSM_Entry.</para>
            /// </param>
            /// <returns>Class instance <see cref="DsmEntry" />.
            ///     <para xml:lang="ru">Экземпляр класса <see cref="DsmEntry" />.</para>
            /// </returns>
            public static DsmEntry Create(IntPtr ptr)
            {
                return new DsmEntry(ptr);
            }

            public TwRC DsmInvoke<T>(TwIdentity origin, TwDG dg, TwDAT dat, TwMSG msg, ref T data) where T : class
            {
                if (data == null) throw new ArgumentNullException();
                var dataGlobal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T)));
                try
                {
                    Marshal.StructureToPtr(data, dataGlobal, true);

                    var rc = DsmRaw(origin, IntPtr.Zero, dg, dat, msg, dataGlobal);
                    if (rc == TwRC.Success) data = (T)Marshal.PtrToStructure(dataGlobal, typeof(T));
                    return rc;
                }
                finally
                {
                    Marshal.FreeHGlobal(dataGlobal);
                }
            }

            public TwRC DsInvoke<T>(TwIdentity origin, TwIdentity dest, TwDG dg, TwDAT dat, TwMSG msg, ref T data)
                where T : class
            {
                if (data == null) throw new ArgumentNullException();
                var dataGlobal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T)));
                try
                {
                    Marshal.StructureToPtr(data, dataGlobal, true);

                    var rc = DsRaw(origin, dest, dg, dat, msg, dataGlobal);
                    if (rc == TwRC.Success || rc == TwRC.DSEvent || rc == TwRC.XferDone)
                        data = (T)Marshal.PtrToStructure(dataGlobal, typeof(T));
                    return rc;
                }
                finally
                {
                    Marshal.FreeHGlobal(dataGlobal);
                }
            }

            #region Properties

            public DsMparent DsmParent { get; }

            public DsMraw DsmRaw { get; }

            public DSixfer DsImageXfer { get; }

            public DSraw DsRaw { get; }

            #endregion

            #region import libtwaindsm.so (Unix)

            [DllImport("/usr/local/lib/libtwaindsm.so", EntryPoint = "DSM_Entry", CharSet = CharSet.Ansi)]
            private static extern TwRC _LinuxDsmParent([In][Out] TwIdentity origin, IntPtr zeroptr, TwDG dg, TwDAT dat,
                TwMSG msg, ref IntPtr refptr);

            [DllImport("/usr/local/lib/libtwaindsm.so", EntryPoint = "DSM_Entry", CharSet = CharSet.Ansi)]
            private static extern TwRC _LinuxDsmRaw([In][Out] TwIdentity origin, IntPtr zeroptr, TwDG dg, TwDAT dat,
                TwMSG msg, IntPtr rawData);

            [DllImport("/usr/local/lib/libtwaindsm.so", EntryPoint = "DSM_Entry", CharSet = CharSet.Ansi)]
            private static extern TwRC _LinuxDsImageXfer([In][Out] TwIdentity origin, [In][Out] TwIdentity dest,
                TwDG dg, TwDAT dat, TwMSG msg, ref IntPtr hbitmap);

            [DllImport("/usr/local/lib/libtwaindsm.so", EntryPoint = "DSM_Entry", CharSet = CharSet.Ansi)]
            private static extern TwRC _LinuxDsRaw([In][Out] TwIdentity origin, [In][Out] TwIdentity dest, TwDG dg,
                TwDAT dat, TwMSG msg, IntPtr arg);

            #endregion

            #region import TWAIN.framework/TWAIN (MacOSX)

            [DllImport("/System/Library/Frameworks/TWAIN.framework/TWAIN", EntryPoint = "DSM_Entry",
                CharSet = CharSet.Ansi)]
            private static extern TwRC _MacosxDsmParent([In][Out] TwIdentity origin, IntPtr zeroptr, TwDG dg,
                TwDAT dat, TwMSG msg, ref IntPtr refptr);

            [DllImport("/System/Library/Frameworks/TWAIN.framework/TWAIN", EntryPoint = "DSM_Entry",
                CharSet = CharSet.Ansi)]
            private static extern TwRC _MacosxDsmRaw([In][Out] TwIdentity origin, IntPtr zeroptr, TwDG dg, TwDAT dat,
                TwMSG msg, IntPtr rawData);

            [DllImport("/System/Library/Frameworks/TWAIN.framework/TWAIN", EntryPoint = "DSM_Entry",
                CharSet = CharSet.Ansi)]
            private static extern TwRC _MacosxDsImageXfer([In][Out] TwIdentity origin, [In][Out] TwIdentity dest,
                TwDG dg, TwDAT dat, TwMSG msg, ref IntPtr hbitmap);

            [DllImport("/System/Library/Frameworks/TWAIN.framework/TWAIN", EntryPoint = "DSM_Entry",
                CharSet = CharSet.Ansi)]
            private static extern TwRC _MacosxDsRaw([In][Out] TwIdentity origin, [In][Out] TwIdentity dest, TwDG dg,
                TwDAT dat, TwMSG msg, IntPtr arg);

            #endregion
        }

        /// <summary>
        ///     Entry points for memory management functions.
        ///     <para xml:lang="ru">Точки входа для функций управления памятью.</para>
        /// </summary>
        internal sealed class _Memory
        {
            private static TwEntryPoint _entryPoint;

            /// <summary>
            ///     Allocates a memory block of the specified size.
            ///     <para xml:lang="ru">Выделяет блок памяти указанного размера.</para>
            /// </summary>
            /// <param name="size">The size of the memory block.
            ///     <para xml:lang="ru">Размер блока памяти.</para>
            /// </param>
            /// <returns>Memory descriptor.
            ///     <para xml:lang="ru">Дескриптор памяти.</para>
            /// </returns>
            public static IntPtr Alloc(int size)
            {
                if (_entryPoint != null && _entryPoint.MemoryAllocate != null) return _entryPoint.MemoryAllocate(size);
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        throw new NotSupportedException();
                    default:
                        return GlobalAlloc(0x42, size);
                }
            }

            /// <summary>
            ///     Frees up memory.
            ///     <para xml:lang="ru">Освобождает память.</para>
            /// </summary>
            /// <param name="handle">Memory descriptor.
            ///     <para xml:lang="ru">Дескриптор памяти.</para>
            /// </param>
            public static void Free(IntPtr handle)
            {
                if (_entryPoint != null && _entryPoint.MemoryFree != null)
                {
                    _entryPoint.MemoryFree(handle);
                    return;
                }

                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        throw new NotSupportedException();
                    default:
                        GlobalFree(handle);
                        break;
                }
            }

            /// <summary>
            ///     Performs a memory lock.
            ///     <para xml:lang="ru">Выполняет блокировку памяти.</para>
            /// </summary>
            /// <param name="handle">Memory descriptor.
            ///     <para xml:lang="ru">Дескриптор памяти.</para>
            /// </param>
            /// <returns>Pointer to a block of memory.
            ///     <para xml:lang="ru">Указатель на блок памяти.</para>
            /// </returns>
            public static IntPtr Lock(IntPtr handle)
            {
                if (_entryPoint != null && _entryPoint.MemoryLock != null) return _entryPoint.MemoryLock(handle);
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        throw new NotSupportedException();
                    default:
                        return GlobalLock(handle);
                }
            }

            /// <summary>
            ///     Unlocks memory.
            ///     <para xml:lang="ru">Выполняет разблокировку памяти.</para>
            /// </summary>
            /// <param name="handle">Memory descriptor.
            ///     <para xml:lang="ru">Дескриптор памяти.</para>
            /// </param>
            public static void Unlock(IntPtr handle)
            {
                if (_entryPoint != null && _entryPoint.MemoryUnlock != null)
                {
                    _entryPoint.MemoryUnlock(handle);
                    return;
                }

                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        throw new NotSupportedException();
                    default:
                        GlobalUnlock(handle);
                        break;
                }
            }

            public static void ZeroMemory(IntPtr dest, IntPtr size)
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        var data = new byte[size.ToInt32()];
                        Marshal.Copy(data, 0, dest, data.Length);
                        break;
                    default:
                        _ZeroMemory(dest, size);
                        break;
                }
            }

            /// <summary>
            ///     Sets entry points.
            ///     <para xml:lang="ru">Устаначливает точки входа.</para>
            /// </summary>
            /// <param name="entry">Entry points.
            ///     <para xml:lang="ru">Точки входа.</para>
            /// </param>
            internal static void _SetEntryPoints(TwEntryPoint entry)
            {
                _entryPoint = entry;
            }

            #region import kernel32.dll

            [DllImport("kernel32.dll", ExactSpelling = true)]
            private static extern IntPtr GlobalAlloc(int flags, int size);

            [DllImport("kernel32.dll", ExactSpelling = true)]
            private static extern IntPtr GlobalLock(IntPtr handle);

            [DllImport("kernel32.dll", ExactSpelling = true)]
            private static extern bool GlobalUnlock(IntPtr handle);

            [DllImport("kernel32.dll", ExactSpelling = true)]
            private static extern IntPtr GlobalFree(IntPtr handle);

            [DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory", SetLastError = false)]
            private static extern void _ZeroMemory(IntPtr dest, IntPtr size);

            #endregion
        }

        /// <summary>
        ///     Entry points for platform features.
        ///     <para xml:lang="ru">Точки входа для функций платформы.</para>
        /// </summary>
        internal sealed class _Platform
        {
            /// <summary>
            ///     Loads the specified library into the process memory.
            ///     <para xml:lang="ru">Загружает указаную библиотеку в память процесса.</para>
            /// </summary>
            /// <param name="fileName">The name of the library.
            ///     <para xml:lang="ru">Имя библиотеки.</para>
            /// </param>
            /// <returns>Module descriptor.
            ///     <para xml:lang="ru">Дескриптор модуля.</para>
            /// </returns>
            internal static IntPtr Load(string fileName)
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        throw new NotSupportedException();
                    default:
                        return LoadLibrary(fileName);
                }
            }

            /// <summary>
            ///     Unloads the specified library from the process memory.
            ///     <para xml:lang="ru">Выгружает указаную библиотеку из памяти процесса.</para>
            /// </summary>
            /// <param name="hModule">Module descriptor
            ///     <para xml:lang="ru">Дескриптор модуля</para>
            /// </param>
            internal static void Unload(IntPtr hModule)
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        break;
                    default:
                        FreeLibrary(hModule);
                        break;
                }
            }

            /// <summary>
            ///     Returns the address of the specified procedure.
            ///     <para xml:lang="ru">Возвращает адрес указанной процедуры.</para>
            /// </summary>
            /// <param name="hModule">Module descriptor.
            ///     <para xml:lang="ru">Дескриптор модуля.</para>
            /// </param>
            /// <param name="procName">The name of the procedure.
            ///     <para xml:lang="ru">Имя процедуры.</para>
            /// </param>
            /// <returns>Pointer to a procedure.
            ///     <para xml:lang="ru">Указатель на процедуру.</para>
            /// </returns>
            internal static IntPtr GetProcAddr(IntPtr hModule, string procName)
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Unix:
                    case PlatformID.MacOSX:
                        throw new NotSupportedException();
                    default:
                        return GetProcAddress(hModule, procName);
                }
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr LoadLibrary(string fileName);

            [DllImport("kernel32.dll", ExactSpelling = true)]
            private static extern bool FreeLibrary(IntPtr hModule);

            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
            private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        }

        /// <summary>
        ///     Win32 message filter.
        ///     <para xml:lang="ru">Фильтр win32-сообщений.</para>
        /// </summary>
        private sealed class MessageFilter : IMessageFilter, IDisposable
        {
            private readonly Twain32 _twain;
            private TwEvent _evtmsg = new TwEvent();
            private bool _isSetFilter;

            public MessageFilter(Twain32 twain)
            {
                _twain = twain;
                _evtmsg.EventPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinMsg)));
            }

            #region IDisposable

            public void Dispose()
            {
                if (_evtmsg != null && _evtmsg.EventPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_evtmsg.EventPtr);
                    _evtmsg.EventPtr = IntPtr.Zero;
                }
            }

            #endregion

            #region IMessageFilter

            public bool PreFilterMessage(ref Message m)
            {
                try
                {
                    if (_twain._srcds.Id == 0) return false;
                    Marshal.StructureToPtr(
                        new WinMsg { hwnd = m.HWnd, message = m.Msg, wParam = m.WParam, lParam = m.LParam },
                        _evtmsg.EventPtr, true);
                    _evtmsg.Message = TwMSG.Null;

                    switch (_twain._dsmEntry.DsInvoke(_twain.AppId, _twain._srcds, TwDG.Control, TwDAT.Event,
                        TwMSG.ProcessEvent, ref _evtmsg))
                    {
                        case TwRC.DSEvent:
                            _twain._TwCallbackProcCore(_evtmsg.Message, isCloseReq =>
                            {
                                if (!isCloseReq && !_twain.DisableAfterAcquire)
                                    return;

                                _RemoveFilter();
                                _twain.DisableDataSource();
                            });
                            break;
                        case TwRC.NotDSEvent:
                            return false;
                        case TwRC.Failure:
                            throw new TwainException(_twain._GetTwainStatus(), TwRC.Failure);
                        default:
                            throw new InvalidOperationException(
                                "Получен неверный код результата операции. Invalid a Return Code value.");
                    }
                }
                catch (TwainException ex)
                {
                    _twain._OnAcquireError(new AcquireErrorEventArgs(ex));
                }
                catch (Exception ex)
                {
                    _twain._OnAcquireError(new AcquireErrorEventArgs(new TwainException(ex.Message, ex)));
                }

                return true;
            }

            #endregion

            public void SetFilter()
            {
                if (!_isSetFilter)
                {
                    _isSetFilter = true;
                    Application.AddMessageFilter(this);
                }
            }

            private void _RemoveFilter()
            {
                Application.RemoveMessageFilter(this);
                _isSetFilter = false;
            }

            [StructLayout(LayoutKind.Sequential, Pack = 2)]
            private struct WinMsg
            {
                public IntPtr hwnd;
                public int message;
                public IntPtr wParam;
                public IntPtr lParam;
            }
        }

        [Serializable]
        private sealed class SealedImage
        {
            private Stream _stream;

            [NonSerialized] private Image _image;
#if !NET2
            [NonSerialized]
            private System.Windows.Media.Imaging.BitmapImage _image2 = null;
#endif

            private SealedImage()
            {
            }

            public static implicit operator SealedImage(Stream stream)
            {
                return new SealedImage { _stream = stream };
            }

            public static implicit operator Stream(SealedImage image)
            {
                image._stream.Seek(0L, SeekOrigin.Begin);
                return image._stream;
            }

            public static implicit operator Image(SealedImage value)
            {
                if (value._image != null)
                    return value._image;

                value._stream.Seek(0L, SeekOrigin.Begin);
                value._image = Image.FromStream(value._stream);
                return value._image;
            }

#if !NET2
            public static implicit operator System.Windows.Media.ImageSource(_Image value) {
                if(value._image2==null) {
                    value._stream.Seek(0L,SeekOrigin.Begin);
                    value._image2 = new System.Windows.Media.Imaging.BitmapImage();
                    value._image2.BeginInit();
                    value._image2.StreamSource = value._stream;
                    value._image2.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    value._image2.EndInit();
                    value._image2.Freeze();
                }
                return value._image2;
            }
#endif
        }

        /// <summary>
        ///     Range of values.
        ///     <para xml:lang="ru">Диапазон значений.</para>
        /// </summary>
        [Serializable]
        public sealed class Range
        {
            /// <summary>
            ///     Prevents a default instance of the <see cref="Range" /> class from being created.
            /// </summary>
            private Range()
            {
            }

            /// <summary>
            ///     Prevents a default instance of the <see cref="Range" /> class from being created.
            /// </summary>
            /// <param name="range">The range.</param>
            private Range(TwRange range)
            {
                MinValue = TwTypeHelper.CastToCommon(range.ItemType,
                    TwTypeHelper.ValueToTw(range.ItemType, range.MinValue));
                MaxValue = TwTypeHelper.CastToCommon(range.ItemType,
                    TwTypeHelper.ValueToTw(range.ItemType, range.MaxValue));
                StepSize = TwTypeHelper.CastToCommon(range.ItemType,
                    TwTypeHelper.ValueToTw(range.ItemType, range.StepSize));
                CurrentValue = TwTypeHelper.CastToCommon(range.ItemType,
                    TwTypeHelper.ValueToTw(range.ItemType, range.CurrentValue));
                DefaultValue = TwTypeHelper.CastToCommon(range.ItemType,
                    TwTypeHelper.ValueToTw(range.ItemType, range.DefaultValue));
            }

            /// <summary>
            ///     Gets or sets the minimum value.
            ///     <para xml:lang="ru">Возвращает или устанавливает минимальное значение.</para>
            /// </summary>
            public object MinValue { get; set; }

            /// <summary>
            ///     Gets or sets the maximum value.
            ///     <para xml:lang="ru">Возвращает или устанавливает максимальное значение.</para>
            /// </summary>
            public object MaxValue { get; set; }

            /// <summary>
            ///     Gets or sets the step.
            ///     <para xml:lang="ru">Возвращает или устанавливает шаг.</para>
            /// </summary>
            public object StepSize { get; set; }

            /// <summary>
            ///     Gets or sets the default value.
            ///     <para xml:lang="ru">Возвращает или устанавливает значае по умолчанию.</para>
            /// </summary>
            public object DefaultValue { get; set; }

            /// <summary>
            ///     Gets or sets the current value.
            ///     <para xml:lang="ru">Возвращает или устанавливает текущее значение.</para>
            /// </summary>
            public object CurrentValue { get; set; }

            /// <summary>
            ///     Creates and returns an instance <see cref="Range" />.
            ///     <para xml:lang="ru">Создает и возвращает экземпляр <see cref="Range" />.</para>
            /// </summary>
            /// <param name="range">Instance <see cref="TwRange" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="TwRange" />.</para>
            /// </param>
            /// <returns>Instance <see cref="Range" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="Range" />.</para>
            /// </returns>
            internal static Range CreateRange(TwRange range)
            {
                return new Range(range);
            }

            /// <summary>
            ///     Creates and returns an instance <see cref="Range" />.
            ///     <para xml:lang="ru">Создает и возвращает экземпляр <see cref="Range" />.</para>
            /// </summary>
            /// <param name="minValue">Minimum value.
            ///     <para xml:lang="ru">Минимальное значение.</para>
            /// </param>
            /// <param name="maxValue">The maximum value.
            ///     <para xml:lang="ru">Максимальное значение.</para>
            /// </param>
            /// <param name="stepSize">Step.
            ///     <para xml:lang="ru">Шаг.</para>
            /// </param>
            /// <param name="defaultValue">The default value.
            ///     <para xml:lang="ru">Значение по умолчанию.</para>
            /// </param>
            /// <param name="currentValue">Present value.
            ///     <para xml:lang="ru">Текущее значение.</para>
            /// </param>
            /// <returns>Instance <see cref="Range" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="Range" />.</para>
            /// </returns>
            public static Range CreateRange(object minValue, object maxValue, object stepSize, object defaultValue,
                object currentValue)
            {
                return new Range
                {
                    MinValue = minValue,
                    MaxValue = maxValue,
                    StepSize = stepSize,
                    DefaultValue = defaultValue,
                    CurrentValue = currentValue
                };
            }

            /// <summary>
            ///     Converts an instance of a class to an instance <see cref="TwRange" />.
            ///     <para xml:lang="ru">Конвертирует экземпляр класса в экземпляр <see cref="TwRange" />.</para>
            /// </summary>
            /// <returns>Instance <see cref="TwRange" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="TwRange" />.</para>
            /// </returns>
            internal TwRange ToTwRange()
            {
                var type = TwTypeHelper.TypeOf(CurrentValue.GetType());
                return new TwRange
                {
                    ItemType = type,
                    MinValue = TwTypeHelper.ValueFromTw<uint>(TwTypeHelper.CastToTw(type, MinValue)),
                    MaxValue = TwTypeHelper.ValueFromTw<uint>(TwTypeHelper.CastToTw(type, MaxValue)),
                    StepSize = TwTypeHelper.ValueFromTw<uint>(TwTypeHelper.CastToTw(type, StepSize)),
                    DefaultValue = TwTypeHelper.ValueFromTw<uint>(TwTypeHelper.CastToTw(type, DefaultValue)),
                    CurrentValue = TwTypeHelper.ValueFromTw<uint>(TwTypeHelper.CastToTw(type, CurrentValue))
                };
            }
        }

        /// <summary>
        ///     Enumeration.
        ///     <para xml:lang="ru">Перечисление.</para>
        /// </summary>
        [Serializable]
        public sealed class Enumeration
        {
            /// <summary>
            ///     Prevents a default instance of the <see cref="Enumeration" /> class from being created.
            /// </summary>
            /// <param name="items">Listing items.
            ///     <para xml:lang="ru">Элементы перечисления.</para>
            /// </param>
            /// <param name="currentIndex">Current index.
            ///     <para xml:lang="ru">Текущий индекс.</para>
            /// </param>
            /// <param name="defaultIndex">The default index.
            ///     <para xml:lang="ru">Индекс по умолчанию.</para>
            /// </param>
            private Enumeration(object[] items, int currentIndex, int defaultIndex)
            {
                Items = items;
                CurrentIndex = currentIndex;
                DefaultIndex = defaultIndex;
            }

            /// <summary>
            ///     Returns the number of items.
            ///     <para xml:lang="ru">Возвращает количество элементов.</para>
            /// </summary>
            public int Count => Items.Length;

            /// <summary>
            ///     Returns the current index.
            ///     <para xml:lang="ru">Возвращает текущий индекс.</para>
            /// </summary>
            public int CurrentIndex { get; private set; }

            /// <summary>
            ///     Returns the default index.
            ///     <para xml:lang="ru">Возвращает индекс по умолчанию.</para>
            /// </summary>
            public int DefaultIndex { get; private set; }

            /// <summary>
            ///     Returns the element at the specified index.
            ///     <para xml:lang="ru">Возвращает элемент по указанному индексу.</para>
            /// </summary>
            /// <param name="index">Index.
            ///     <para xml:lang="ru">Индекс.</para>
            /// </param>
            /// <returns>The item at the specified index.
            ///     <para xml:lang="ru">Элемент по указанному индексу.</para>
            /// </returns>
            public object this[int index]
            {
                get => Items[index];
                internal set => Items[index] = value;
            }

            internal object[] Items { get; }

            /// <summary>
            ///     Creates and returns an instance <see cref="Enumeration" />.
            ///     <para xml:lang="ru">Создает и возвращает экземпляр <see cref="Enumeration" />.</para>
            /// </summary>
            /// <param name="items">Listing items.
            ///     <para xml:lang="ru">Элементы перечисления.</para>
            /// </param>
            /// <param name="currentIndex">Current index.
            ///     <para xml:lang="ru">Текущий индекс.</para>
            /// </param>
            /// <param name="defaultIndex">The default index.
            ///     <para xml:lang="ru">Индекс по умолчанию.</para>
            /// </param>
            /// <returns>Instance <see cref="Enumeration" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="Enumeration" />.</para>
            /// </returns>
            public static Enumeration CreateEnumeration(object[] items, int currentIndex, int defaultIndex)
            {
                return new Enumeration(items, currentIndex, defaultIndex);
            }

            /// <summary>
            ///     Creates and returns an instance <see cref="Enumeration" />.
            ///     <para xml:lang="ru">Создает и возвращает экземпляр <see cref="Enumeration" />.</para>
            /// </summary>
            /// <param name="value">Instance <see cref="Range" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="Range" />.</para>
            /// </param>
            /// <returns>Instance <see cref="Enumeration" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="Enumeration" />.</para>
            /// </returns>
            public static Enumeration FromRange(Range value)
            {
                int currentIndex = 0, defaultIndex = 0;
                var items = new object[(int)((Convert.ToSingle(value.MaxValue) - Convert.ToSingle(value.MinValue)) /
                                              Convert.ToSingle(value.StepSize)) + 1];
                for (var i = 0; i < items.Length; i++)
                {
                    items[i] = Convert.ToSingle(value.MinValue) + Convert.ToSingle(value.StepSize) * i;
                    var item = Convert.ToSingle(items[i]);
                    var currentValue = Convert.ToSingle(value.CurrentValue);
                    var defaultValue = Convert.ToSingle(value.DefaultValue);
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (item == currentValue) currentIndex = i;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (item == defaultValue) defaultIndex = i;
                }

                return CreateEnumeration(items, currentIndex, defaultIndex);
            }

            /// <summary>
            ///     Creates and returns an instance <see cref="Enumeration" />.
            ///     <para xml:lang="ru">Создает и возвращает экземпляр <see cref="Enumeration" />.</para>
            /// </summary>
            /// <param name="value">An array of values.
            ///     <para xml:lang="ru">Массив значений.</para>
            /// </param>
            /// <returns>Instance <see cref="Enumeration" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="Enumeration" />.</para>
            /// </returns>
            public static Enumeration FromArray(object[] value)
            {
                return CreateEnumeration(value, 0, 0);
            }

            /// <summary>
            ///     Creates and returns an instance <see cref="Enumeration" />.
            ///     <para xml:lang="ru">Создает и возвращает экземпляр <see cref="Enumeration" />.</para>
            /// </summary>
            /// <param name="value">Value.
            ///     <para xml:lang="ru">Значение.</para>
            /// </param>
            /// <returns>Instance <see cref="Enumeration" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="Enumeration" />.</para>
            /// </returns>
            public static Enumeration FromOneValue(ValueType value)
            {
                return CreateEnumeration(new object[] { value }, 0, 0);
            }

            internal static Enumeration FromObject(object value)
            {
                switch (value)
                {
                    case Range range:
                        return FromRange(range);
                    case object[] objects:
                        return FromArray(objects);
                    case ValueType type:
                        return FromOneValue(type);
                    case string _:
                        return CreateEnumeration(new[] { value }, 0, 0);
                    default:
                        return value as Enumeration;
                }
            }
        }

        /// <summary>
        ///     Description of the image.
        ///     <para xml:lang="ru">Описание изображения.</para>
        /// </summary>
        [Serializable]
        public sealed class ImageInfo
        {
            private ImageInfo()
            {
            }

            /// <summary>
            ///     Resolution in the horizontal
            /// </summary>
            public float XResolution { get; private set; }

            /// <summary>
            ///     Resolution in the vertical
            /// </summary>
            public float YResolution { get; private set; }

            /// <summary>
            ///     Columns in the image, -1 if unknown by DS
            /// </summary>
            public int ImageWidth { get; private set; }

            /// <summary>
            ///     Rows in the image, -1 if unknown by DS
            /// </summary>
            public int ImageLength { get; private set; }

            /// <summary>
            ///     Number of bits for each sample
            /// </summary>
            public short[] BitsPerSample { get; private set; }

            /// <summary>
            ///     Number of bits for each padded pixel
            /// </summary>
            public short BitsPerPixel { get; private set; }

            /// <summary>
            ///     True if Planar, False if chunky
            /// </summary>
            public bool Planar { get; private set; }

            /// <summary>
            ///     How to interp data; photo interp
            /// </summary>
            public TwPixelType PixelType { get; private set; }

            /// <summary>
            ///     How the data is compressed
            /// </summary>
            public TwCompression Compression { get; private set; }

            /// <summary>
            ///     Creates and returns a new instance of the ImageInfo class based on an instance of the TwImageInfo class.
            ///     <para xml:lang="ru">Создает и возвращает новый экземпляр класса ImageInfo на основе экземпляра класса TwImageInfo.</para>
            /// </summary>
            /// <param name="info">Description of the image.
            ///     <para xml:lang="ru">Описание изображения.</para>
            /// </param>
            /// <returns>An instance of the ImageInfo class.
            ///     <para xml:lang="ru">Экземпляр класса ImageInfo.</para>
            /// </returns>
            internal static ImageInfo FromTwImageInfo(TwImageInfo info)
            {
                return new ImageInfo
                {
                    BitsPerPixel = info.BitsPerPixel,
                    BitsPerSample = _Copy(info.BitsPerSample, info.SamplesPerPixel),
                    Compression = info.Compression,
                    ImageLength = info.ImageLength,
                    ImageWidth = info.ImageWidth,
                    PixelType = info.PixelType,
                    Planar = info.Planar,
                    XResolution = info.XResolution,
                    YResolution = info.YResolution
                };
            }

            private static short[] _Copy(short[] array, int len)
            {
                var result = new short[len];
                for (var i = 0; i < len; i++) result[i] = array[i];
                return result;
            }
        }

        /// <summary>
        ///     Extended image description.
        ///     <para xml:lang="ru">Расширенное описание изображения.</para>
        /// </summary>
        [Serializable]
        public sealed class ExtImageInfo : Collection<ExtImageInfo.InfoItem>
        {
            private ExtImageInfo()
            {
            }

            /// <summary>
            ///     Creates and returns an instance of the ExtImageInfo class from an unmanaged memory block.
            ///     <para xml:lang="ru">Создает и возвращает экземпляр класса ExtImageInfo из блока неуправляемой памяти.</para>
            /// </summary>
            /// <param name="ptr">Pointer to an unmanaged memory block.
            ///     <para xml:lang="ru">Указатель на блок неуправляемой памяти.</para>
            /// </param>
            /// <returns>An instance of the ExtImageInfo class.
            ///     <para xml:lang="ru">Экземпляр класса ExtImageInfo.</para>
            /// </returns>
            internal static ExtImageInfo FromPtr(IntPtr ptr)
            {
                var twExtImageInfoSize = Marshal.SizeOf(typeof(TwExtImageInfo));
                var twInfoSize = Marshal.SizeOf(typeof(TwInfo));
                var extImageInfo = Marshal.PtrToStructure(ptr, typeof(TwExtImageInfo)) as TwExtImageInfo;
                var result = new ExtImageInfo();

                if (extImageInfo == null)
                    return result;

                for (var i = 0; i < extImageInfo.NumInfos; i++)
                    using (var item =
                        Marshal.PtrToStructure((IntPtr)(ptr.ToInt64() + twExtImageInfoSize + twInfoSize * i),
                            typeof(TwInfo)) as TwInfo)
                    {
                        result.Add(InfoItem.FromTwInfo(item));
                    }

                return result;
            }

            /// <summary>
            ///     Description element for extended image information.
            ///     <para xml:lang="ru">Элемент описания расширенной информации о изображении.</para>
            /// </summary>
            [Serializable]
            [DebuggerDisplay("InfoId = {InfoId}, IsSuccess = {IsSuccess}, Value = {Value}")]
            public sealed class InfoItem
            {
                private InfoItem()
                {
                }

                /// <summary>
                ///     Returns a code for extended image information.
                ///     <para xml:lang="ru">Возвращает код расширенной информации о изображении.</para>
                /// </summary>
                public TwEI InfoId { get; private set; }

                /// <summary>
                ///     Вreturns true if the requested information is not supported by the data source; otherwise false.
                ///     <para xml:lang="ru">Возвращает true, если запрошенная информация не поддерживается источником данных; иначе, false.</para>
                /// </summary>
                public bool IsNotSupported { get; private set; }

                /// <summary>
                ///     Returns true if the requested information is supported by the data source but is currently unavailable; otherwise
                ///     false.
                ///     <para xml:lang="ru">
                ///         Возвращает true, если запрошенная информация поддерживается источником данных, но в данный
                ///         момент недоступна; иначе, false.
                ///     </para>
                /// </summary>
                public bool IsNotAvailable { get; private set; }

                /// <summary>
                ///     Returns true if the requested information was successfully retrieved; otherwise false.
                ///     <para xml:lang="ru">Возвращает true, если запрошенная информация была успешно извлечена; иначе, false.</para>
                /// </summary>
                public bool IsSuccess { get; private set; }

                /// <summary>
                ///     Returns the value of an element.
                ///     <para xml:lang="ru">Возвращает значение элемента.</para>
                /// </summary>
                public object Value { get; private set; }

                /// <summary>
                ///     Creates and returns an instance class of an extended image information description element from an internal
                ///     instance of an extended image information description element class.
                ///     <para xml:lang="ru">
                ///         Создает и возвращает экземпляр класса элемента описания расширенной информации о изображении из
                ///         внутреннего экземпляра класса элемента описания расширенной информации о изображении.
                ///     </para>
                /// </summary>
                /// <param name="info">
                ///     An internal instance of the extended image information description element class.
                ///     <para xml:lang="ru">Внутрений экземпляр класса элемента описания расширенной информации о изображении.</para>
                /// </param>
                /// <returns>
                ///     An instance of the extended image information description item class.
                ///     <para xml:lang="ru">Экземпляр класса элемента описания расширенной информации о изображении.</para>
                /// </returns>
                internal static InfoItem FromTwInfo(TwInfo info)
                {
                    return new InfoItem
                    {
                        InfoId = info.InfoId,
                        IsNotSupported = info.ReturnCode == TwRC.InfoNotSupported,
                        IsNotAvailable = info.ReturnCode == TwRC.DataNotAvailable,
                        IsSuccess = info.ReturnCode == TwRC.Success,
                        Value = info.GetValue()
                    };
                }
            }
        }

        /// <summary>
        ///     Used to pass image data (e.g. in strips) from DS to application.
        /// </summary>
        [Serializable]
        public sealed class ImageMemXfer
        {
            private ImageMemXfer()
            {
            }

            /// <summary>
            ///     How the data is compressed.
            /// </summary>
            public TwCompression Compression { get; private set; }

            /// <summary>
            ///     Number of bytes in a row of data.
            /// </summary>
            public uint BytesPerRow { get; private set; }

            /// <summary>
            ///     How many columns.
            /// </summary>
            public uint Columns { get; private set; }

            /// <summary>
            ///     How many rows.
            /// </summary>
            public uint Rows { get; private set; }

            /// <summary>
            ///     How far from the side of the image.
            /// </summary>
            public uint XOffset { get; private set; }

            /// <summary>
            ///     How far from the top of the image.
            /// </summary>
            public uint YOffset { get; private set; }

            /// <summary>
            ///     Data.
            /// </summary>
            public byte[] ImageData { get; private set; }

            internal static ImageMemXfer Create(TwImageMemXfer data)
            {
                var res = new ImageMemXfer
                {
                    BytesPerRow = data.BytesPerRow,
                    Columns = data.Columns,
                    Compression = data.Compression,
                    Rows = data.Rows,
                    XOffset = data.XOffset,
                    YOffset = data.YOffset
                };
                if ((data.Memory.Flags & TwMF.Handle) != 0)
                {
                    var memData = _Memory.Lock(data.Memory.TheMem);
                    try
                    {
                        res.ImageData = new byte[data.BytesWritten];
                        Marshal.Copy(memData, res.ImageData, 0, res.ImageData.Length);
                    }
                    finally
                    {
                        _Memory.Unlock(data.Memory.TheMem);
                    }
                }
                else
                {
                    res.ImageData = new byte[data.BytesWritten];
                    Marshal.Copy(data.Memory.TheMem, res.ImageData, 0, res.ImageData.Length);
                }

                return res;
            }
        }

        /// <summary>
        ///     Description of the image file.
        ///     <para xml:lang="ru">Описание файла изображения.</para>
        /// </summary>
        [Serializable]
        public sealed class ImageFileXfer
        {
            /// <summary>
            ///     Initializes a new instance <see cref="ImageFileXfer" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр <see cref="ImageFileXfer" />.</para>
            /// </summary>
            private ImageFileXfer()
            {
            }

            /// <summary>
            ///     Returns the file name.
            ///     <para xml:lang="ru">Возвращает имя файла.</para>
            /// </summary>
            public string FileName { get; private set; }

            /// <summary>
            ///     Returns the file format.
            ///     <para xml:lang="ru">Фозвращает формат файла.</para>
            /// </summary>
            public TwFF Format { get; private set; }

            /// <summary>
            ///     Creates and returns a new instance <see cref="ImageFileXfer" />.
            ///     <para xml:lang="ru">Создает и возвращает новый экземпляр <see cref="ImageFileXfer" />.</para>
            /// </summary>
            /// <param name="data">File description.
            ///     <para xml:lang="ru">Описание файла.</para>
            /// </param>
            /// <returns>Instance <see cref="ImageFileXfer" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="ImageFileXfer" />.</para>
            /// </returns>
            internal static ImageFileXfer Create(TwSetupFileXfer data)
            {
                return new ImageFileXfer
                {
                    FileName = data.FileName,
                    Format = data.Format
                };
            }
        }

        /// <summary>
        ///     A set of operations for working with a color palette.
        ///     <para xml:lang="ru">Набор операций для работы с цветовой палитрой.</para>
        /// </summary>
        public sealed class TwainPalette : MarshalByRefObject
        {
            private readonly Twain32 _twain;

            /// <summary>
            ///     Initializes a new instance of the class <see cref="TwainPalette" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр класса <see cref="TwainPalette" />.</para>
            /// </summary>
            /// <param name="twain">Class instance <see cref="TwainPalette" />.
            ///     <para xml:lang="ru">Экземпляр класса <see cref="TwainPalette" />.</para>
            /// </param>
            internal TwainPalette(Twain32 twain)
            {
                _twain = twain;
            }

            /// <summary>
            ///     Returns the current color palette.
            ///     <para xml:lang="ru">Возвращает текущую цветовую палитру.</para>
            /// </summary>
            /// <returns>Class instance <see cref="TwainPalette" />.
            ///     <para xml:lang="ru">Экземпляр класса <see cref="TwainPalette" />.</para>
            /// </returns>
            public ColorPalette Get()
            {
                var palette = new TwPalette8();
                var rc = _twain._dsmEntry.DsInvoke(_twain.AppId, _twain._srcds, TwDG.Image, TwDAT.Palette8, TwMSG.Get,
                    ref palette);
                if (rc != TwRC.Success) throw new TwainException(_twain._GetTwainStatus(), rc);
                return palette;
            }

            /// <summary>
            ///     Returns the current default color palette.
            ///     <para xml:lang="ru">Возвращает текущую цветовую палитру, используемую по умолчанию.</para>
            /// </summary>
            /// <returns>Class instance <see cref="TwainPalette" />.
            ///     <para xml:lang="ru">Экземпляр класса <see cref="TwainPalette" />.</para>
            /// </returns>
            public ColorPalette GetDefault()
            {
                var palette = new TwPalette8();
                var rc = _twain._dsmEntry.DsInvoke(_twain.AppId, _twain._srcds, TwDG.Image, TwDAT.Palette8,
                    TwMSG.GetDefault, ref palette);
                if (rc != TwRC.Success) throw new TwainException(_twain._GetTwainStatus(), rc);
                return palette;
            }

            /// <summary>
            ///     Resets the current color palette and sets the specified one.
            ///     <para xml:lang="ru">Сбрасывает текущую цветовую палитру и устанавливает указанную.</para>
            /// </summary>
            /// <param name="palette">Class instance <see cref="TwainPalette" />.
            ///     <para xml:lang="ru">Экземпляр класса <see cref="TwainPalette" />.</para>
            /// </param>
            public void Reset(ColorPalette palette)
            {
                var rc = _twain._dsmEntry.DsInvoke(_twain.AppId, _twain._srcds, TwDG.Image, TwDAT.Palette8, TwMSG.Reset,
                    ref palette);
                if (rc != TwRC.Success) throw new TwainException(_twain._GetTwainStatus(), rc);
            }

            /// <summary>
            ///     Sets the specified color palette.
            ///     <para xml:lang="ru">Устанавливает указанную цветовую палитру.</para>
            /// </summary>
            /// <param name="palette">Class instance <see cref="TwainPalette" />.
            ///     <para xml:lang="ru">Экземпляр класса <see cref="TwainPalette" />.</para>
            /// </param>
            public void Set(ColorPalette palette)
            {
                var rc = _twain._dsmEntry.DsInvoke(_twain.AppId, _twain._srcds, TwDG.Image, TwDAT.Palette8, TwMSG.Set,
                    ref palette);
                if (rc != TwRC.Success) throw new TwainException(_twain._GetTwainStatus(), rc);
            }
        }

        /// <summary>
        ///     Color palette.
        ///     <para xml:lang="ru">Цветовая палитра.</para>
        /// </summary>
        [Serializable]
        public sealed class ColorPalette
        {
            /// <summary>
            ///     Initializes a new instance <see cref="ColorPalette" />.
            ///     <para xml:lang="ru">Инициализирует новый экземпляр <see cref="ColorPalette" />.</para>
            /// </summary>
            private ColorPalette()
            {
            }

            /// <summary>
            ///     Returns the type of palette.
            ///     <para xml:lang="ru">Возвращает тип палитры.</para>
            /// </summary>
            public TwPA PaletteType { get; private set; }

            /// <summary>
            ///     Returns the colors that make up the palette.
            ///     <para xml:lang="ru">Возвращает цвета, входящие в состав палитры.</para>
            /// </summary>
            public Color[] Colors { get; private set; }

            /// <summary>
            ///     Creates and returns a new instance <see cref="ColorPalette" />.
            ///     <para xml:lang="ru">Создает и возвращает новый экземпляр <see cref="ColorPalette" />.</para>
            /// </summary>
            /// <param name="palette">Color palette.
            ///     <para xml:lang="ru">Цветовая палитра.</para>
            /// </param>
            /// <returns>Instance <see cref="ColorPalette" />.
            ///     <para xml:lang="ru">Экземпляр <see cref="ColorPalette" />.</para>
            /// </returns>
            internal static ColorPalette Create(TwPalette8 palette)
            {
                var result = new ColorPalette
                {
                    PaletteType = palette.PaletteType,
                    Colors = new Color[palette.NumColors]
                };
                for (var i = 0; i < palette.NumColors; i++) result.Colors[i] = palette.Colors[i];
                return result;
            }
        }

        /// <summary>
        ///     Identifies the resource.
        /// </summary>
        [Serializable]
        [DebuggerDisplay("{Name}, Version = {Version}")]
        public sealed class Identity
        {
            /// <summary>
            ///     Initializes a new instance of the <see cref="Identity" /> class.
            /// </summary>
            /// <param name="identity">The identity.</param>
            internal Identity(TwIdentity identity)
            {
                Family = identity.ProductFamily;
                Manufacturer = identity.Manufacturer;
                Name = identity.ProductName;
                ProtocolVersion = new Version(identity.ProtocolMajor, identity.ProtocolMinor);
                Version = new Version(identity.Version.MajorNum, identity.Version.MinorNum);
            }

            /// <summary>
            ///     Get the version of the software.
            /// </summary>
            /// <value>
            ///     The version.
            /// </value>
            public Version Version { get; private set; }

            /// <summary>
            ///     Get the protocol version.
            /// </summary>
            /// <value>
            ///     The protocol version.
            /// </value>
            public Version ProtocolVersion { get; private set; }

            /// <summary>
            ///     Get manufacturer name, e.g. "Hewlett-Packard".
            /// </summary>
            public string Manufacturer { get; private set; }

            /// <summary>
            ///     Get product family name, e.g. "ScanJet".
            /// </summary>
            public string Family { get; private set; }

            /// <summary>
            ///     Get product name, e.g. "ScanJet Plus".
            /// </summary>
            public string Name { get; private set; }
        }

        #endregion

        #region Delegates

        #region DSM delegates DAT_ variants

        private delegate TwRC DsMparent([In][Out] TwIdentity origin, IntPtr zeroptr, TwDG dg, TwDAT dat, TwMSG msg,
            ref IntPtr refptr);

        private delegate TwRC DsMraw([In][Out] TwIdentity origin, IntPtr zeroptr, TwDG dg, TwDAT dat, TwMSG msg,
            IntPtr rawData);

        #endregion

        #region DS delegates DAT_ variants to DS

        private delegate TwRC DSixfer([In][Out] TwIdentity origin, [In][Out] TwIdentity dest, TwDG dg, TwDAT dat,
            TwMSG msg, ref IntPtr hbitmap);

        private delegate TwRC DSraw([In][Out] TwIdentity origin, [In][Out] TwIdentity dest, TwDG dg, TwDAT dat,
            TwMSG msg, IntPtr arg);

        #endregion

        internal delegate ImageInfo GetImageInfoCallback();

        internal delegate ExtImageInfo GetExtImageInfoCallback(TwEI[] extInfo);

        private delegate void Action<T>(T arg);

        #endregion
    }
}