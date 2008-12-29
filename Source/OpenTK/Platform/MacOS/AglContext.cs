//
//  
//  AglContext.cs
//
//  Created by Erik Ylvisaker on 3/17/08.
//  Copyright 2008. All rights reserved.
//
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Control = System.Windows.Forms.Control;

namespace OpenTK.Platform.MacOS
{
    using Carbon;
    using Graphics;
    using Graphics.OpenGL;

    using AGLRendererInfo = IntPtr;
    using AGLPixelFormat = IntPtr;
    using AGLContext = IntPtr;
    using AGLPbuffer = IntPtr;

    class AglContext : IGraphicsContext, IGraphicsContextInternal 
    {
        IntPtr contextRef;
        bool mVSync = false;
        IntPtr displayID;
        
        GraphicsMode mode;
		CarbonWindowInfo carbonWindow;
		IGraphicsContext shareContext;
		
        static AglContext()
        {
            if (GraphicsContext.GetCurrentContext == null)
                GraphicsContext.GetCurrentContext = AglContext.GetCurrentContext;
        }
        public AglContext(GraphicsMode mode, IWindowInfo window, IGraphicsContext shareContext)
        {
        	this.mode = mode;
        	this.carbonWindow = (CarbonWindowInfo)window;
        	this.shareContext = shareContext;
        	
        	CreateContext(mode, carbonWindow, shareContext);
        }


        private void AddPixelAttrib(List<int> aglAttributes, Agl.PixelFormatAttribute pixelFormatAttribute)
        {
            Debug.Print(pixelFormatAttribute.ToString());

            aglAttributes.Add((int)pixelFormatAttribute);
        }
        private void AddPixelAttrib(List<int> aglAttributes, Agl.PixelFormatAttribute pixelFormatAttribute, int value)
        {
            Debug.Print("{0} : {1}", pixelFormatAttribute, value);

            aglAttributes.Add((int)pixelFormatAttribute);
            aglAttributes.Add(value);
        }
        void CreateContext(GraphicsMode mode, CarbonWindowInfo carbonWindow, IGraphicsContext shareContext)
        {
            List<int> aglAttributes = new  List<int>();

            Debug.Print("AGL attributes:");
            Debug.Indent();

            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_RGBA);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_DOUBLEBUFFER);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_RED_SIZE, mode.ColorFormat.Red);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_GREEN_SIZE, mode.ColorFormat.Green);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_BLUE_SIZE, mode.ColorFormat.Blue);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ALPHA_SIZE, mode.ColorFormat.Alpha);

            if (mode.Depth > 0)
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_DEPTH_SIZE, mode.Depth);

            if (mode.Stencil > 0)
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_STENCIL_SIZE, mode.Stencil);

            if (mode.AccumulatorFormat.BitsPerPixel > 0)
            {
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ACCUM_RED_SIZE, mode.AccumulatorFormat.Red);
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ACCUM_GREEN_SIZE, mode.AccumulatorFormat.Green);
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ACCUM_BLUE_SIZE, mode.AccumulatorFormat.Blue);
                AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_ACCUM_ALPHA_SIZE, mode.AccumulatorFormat.Alpha);
            }
            //AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_FULLSCREEN);
            AddPixelAttrib(aglAttributes, Agl.PixelFormatAttribute.AGL_NONE);

            Debug.Unindent();

            Debug.Write("Attribute array:  ");
            for (int i = 0; i < aglAttributes.Count; i++)
                Debug.Write(aglAttributes[i].ToString() + "  ");
            Debug.WriteLine("");

            AGLPixelFormat myAGLPixelFormat;
            IntPtr shareContextRef = IntPtr.Zero;

            // Choose a pixel format with the attributes we specified.
            myAGLPixelFormat = Agl.aglChoosePixelFormat(IntPtr.Zero, 0, aglAttributes.ToArray());
            MyAGLReportError("aglChoosePixelFormat");

            if (shareContext != null)
            {
                Debug.Print("shareContext type is {0}", shareContext.GetType());
            }

            if (shareContext != null && shareContext is AglContext)
                shareContextRef = ((AglContext)shareContext).contextRef;

            // create the context and share it with the share reference.
            this.contextRef = Agl.aglCreateContext(myAGLPixelFormat, shareContextRef);
            MyAGLReportError("aglCreateContext");

            // Free the pixel format from memory.
            Agl.aglDestroyPixelFormat(myAGLPixelFormat);
            MyAGLReportError("aglDestroyPixelFormat");

            Debug.Print("IsControl: {0}", carbonWindow.IsControl);
            
            SetDrawable(carbonWindow);
            SetBufferRect(carbonWindow);
			Update(carbonWindow);
			
            MakeCurrent(carbonWindow);

            Debug.Print("context: {0}", contextRef);
        }

        void SetBufferRect(CarbonWindowInfo carbonWindow)
        {
            if (carbonWindow.IsControl == false)
            	return;
            
            System.Windows.Forms.Control ctrl = Control.FromHandle(carbonWindow.WindowRef);
				
            if (ctrl.TopLevelControl == null)
            	return;
            	
            Rect rect = API.GetControlBounds(carbonWindow.WindowRef);

			rect.X = (short)ctrl.Left;
			rect.Y = (short)ctrl.Top;
            
            Debug.Print("Setting buffer_rect for control.");
            Debug.Print("MacOS Coordinate Rect:   {0}", rect);
            
            rect.Y = (short)(ctrl.TopLevelControl.ClientSize.Height - rect.Y - rect.Height);
            Debug.Print("  AGL Coordinate Rect:   {0}", rect);
            
            int[] glrect = new int[4];

            glrect[0] = rect.X;
            glrect[1] = rect.Y;
            glrect[2] = rect.Width;
            glrect[3] = rect.Height;

            Agl.aglSetInteger(contextRef, Agl.ParameterNames.AGL_BUFFER_RECT, glrect);
            MyAGLReportError("aglSetInteger");

            Agl.aglEnable(contextRef, Agl.ParameterNames.AGL_BUFFER_RECT);
            MyAGLReportError("aglEnable");
  
        }
		void SetDrawable(CarbonWindowInfo carbonWindow)
		{
			IntPtr windowPort;
			
            if (carbonWindow.IsControl)
            {
                IntPtr controlOwner = API.GetControlOwner(carbonWindow.WindowRef);

                Debug.Print("GetControlOwner: {0}", controlOwner);

                windowPort = API.GetWindowPort(controlOwner);
            }
            else
                windowPort = API.GetWindowPort(carbonWindow.WindowRef);

            Agl.aglSetDrawable(contextRef, windowPort);

            MyAGLReportError("aglSetDrawable");
		
		}
        public void Update(IWindowInfo window)
        {
            CarbonWindowInfo carbonWindow = (CarbonWindowInfo)window;
			
			SetBufferRect(carbonWindow);
			
            //Agl.aglSetCurrentContext(contextRef);
            Agl.aglUpdateContext(contextRef);
            
        }
        void MyAGLReportError(string function)
        {
            Agl.AglError err = Agl.GetError();

            if (err != Agl.AglError.NoError)
                throw new MacOSException((OSStatus)err, string.Format(
                    "AGL Error from function {0}: {1}  {2}", err, Agl.ErrorString(err)));
        }

        static ContextHandle GetCurrentContext()
        {
            return Agl.aglGetCurrentContext();
        }

        #region IGraphicsContext Members
		bool first = false;
        public void SwapBuffers()
        {
        	if (first == false && carbonWindow.IsControl)
        	{
        		Debug.WriteLine("--> Resetting drawable. <--");
        		first = true;
        		SetDrawable(carbonWindow);
        		Update(carbonWindow);
        	}
        	
            Agl.aglSwapBuffers(contextRef);
            MyAGLReportError("aglSwapBuffers");
        }
		
        public void MakeCurrent(IWindowInfo window)
        {
            if (Agl.aglSetCurrentContext(contextRef) == false)
                MyAGLReportError("aglSetCurrentContext");
        }

        public bool IsCurrent
        {
            get
            {
                return (contextRef == Agl.aglGetCurrentContext());
            }
        }

        public event DestroyEvent<IGraphicsContext> Destroy;
        void OnDestroy()
        {

            if (Destroy != null)
            {
                Debug.Print("Destroy handlers: {0}", Destroy.GetInvocationList().Length);
                Destroy(this, EventArgs.Empty);
            }
        }

        public bool VSync
        {
            get
            {
                return mVSync;
            }
            set
            {
                int intVal = value ? 1 : 0;

                Agl.aglSetInteger(this.contextRef, Agl.ParameterNames.AGL_SWAP_INTERVAL, ref intVal);

                mVSync = value;
            }
        }

        #endregion

        #region IDisposable Members
        ~AglContext()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            
            if (contextRef == IntPtr.Zero)
                return;

            OnDestroy();
            Debug.Print("Disposing of AGL context.");

            try
            {
                throw new Exception();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
            }

            Agl.aglSetCurrentContext(IntPtr.Zero);
            Agl.aglSetDrawable(contextRef, IntPtr.Zero);

            Debug.Print("Set drawable to null for context {0}.", contextRef);

            if (Agl.aglDestroyContext(contextRef) == true)
            {
                contextRef = IntPtr.Zero;
                return;
            }

            // failed to destroy context.
            Debug.WriteLine("Failed to destroy context.");
            Debug.WriteLine(Agl.ErrorString(Agl.GetError()));

            // don't throw an exception from the finalizer thread.
            if (disposing)
            {
                throw new MacOSException((OSStatus)Agl.GetError(), Agl.ErrorString(Agl.GetError()));
            }
        }

        #endregion

        #region IGraphicsContextInternal Members

        void IGraphicsContextInternal.LoadAll()
        {
            GL.LoadAll();
            Glu.LoadAll();
        }

        ContextHandle IGraphicsContextInternal.Context
        {
            get { return contextRef; }
        }

        GraphicsMode IGraphicsContextInternal.GraphicsMode
        {
            get { throw new Exception("The method or operation is not implemented."); }
        }

        void IGraphicsContextInternal.RegisterForDisposal(IDisposable resource)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        void IGraphicsContextInternal.DisposeResources()
        {
            throw new Exception("The method or operation is not implemented.");
        }

        IntPtr IGraphicsContextInternal.GetAddress(string function)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        #endregion

    }
}
