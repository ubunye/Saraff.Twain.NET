using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Saraff.Twain
{
    /// <summary>
    ///     Base class to processing of a acquired image.
    /// </summary>
    /// <seealso cref="Saraff.Twain.IImageHandler" />
    internal abstract class _ImageHandler : IImageHandler
    {
        private const string _ImagePointer = "ImagePointer";

        /// <summary>
        ///     Gets the size of the buffer.
        /// </summary>
        /// <value>
        ///     The size of the buffer.
        /// </value>
        protected abstract int BufferSize { get; }

        /// <summary>
        ///     Gets the state of the handler.
        /// </summary>
        /// <value>
        ///     The state of the handler.
        /// </value>
        protected Dictionary<string, object> HandlerState { get; private set; }

        /// <summary>
        ///     Gets the pointer to unmanaged memory that contain image data.
        /// </summary>
        /// <value>
        ///     The image pointer.
        /// </value>
        protected IntPtr ImagePointer => (IntPtr)HandlerState[_ImagePointer];

        #region IImageHandler

        /// <summary>
        ///     Convert a block of unmanaged memory to stream.
        /// </summary>
        /// <param name="ptr">The pointer to block of unmanaged memory.</param>
        /// <param name="provider">The provider of a streams.</param>
        /// <returns>
        ///     Stream that contains data of a image.
        /// </returns>
        public Stream PtrToStream(IntPtr ptr, IStreamProvider provider)
        {
            HandlerState = new Dictionary<string, object>
            {
                {_ImagePointer, ptr}
            };

            var stream = provider != null ? provider.GetStream() : new MemoryStream();
            PtrToStreamCore(ptr, stream);
            return stream;
        }

        #endregion

        /// <summary>
        ///     Convert a block of unmanaged memory to stream.
        /// </summary>
        /// <param name="ptr">The pointer to block of unmanaged memory.</param>
        /// <param name="stream">The provider of a streams.</param>
        protected virtual void PtrToStreamCore(IntPtr ptr, Stream stream)
        {
            var writer = new BinaryWriter(stream);

            var size = GetSize();
            var buffer = new byte[BufferSize];
            int len;
            for (var offset = 0; offset < size; offset += len)
            {
                len = Math.Min(BufferSize, size - offset);
                Marshal.Copy((IntPtr)(ptr.ToInt64() + offset), buffer, 0, len);
                writer.Write(buffer, 0, len);
            }
        }

        /// <summary>
        ///     Gets the size of a image data.
        /// </summary>
        /// <returns>Size of a image data.</returns>
        protected abstract int GetSize();
    }

    /// <summary>
    ///     Provides processing of a acquired image.
    /// </summary>
    public interface IImageHandler
    {
        /// <summary>
        ///     Convert a block of unmanaged memory to stream.
        /// </summary>
        /// <param name="ptr">The pointer to block of unmanaged memory.</param>
        /// <param name="provider">The provider of a streams.</param>
        /// <returns>Stream that contains data of a image.</returns>
        Stream PtrToStream(IntPtr ptr, IStreamProvider provider);
    }

    /// <summary>
    ///     Provides instances of the <see cref="System.IO.Stream" /> for data writing.
    /// </summary>
    public interface IStreamProvider
    {
        /// <summary>
        ///     Gets the stream.
        /// </summary>
        /// <returns>The stream.</returns>
        Stream GetStream();
    }

    public interface IImageFactory<T>
    {
        T Create(Stream stream);
    }
}