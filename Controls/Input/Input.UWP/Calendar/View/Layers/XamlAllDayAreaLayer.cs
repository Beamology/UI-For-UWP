﻿using System;
using System.Collections.Generic;
using System.Linq;
using Telerik.Core;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Telerik.UI.Xaml.Controls.Input.Calendar
{
    internal class XamlAllDayAreaLayer : CalendarLayer
    {
        internal bool shouldArrange;

        private static SolidColorBrush DefaultBackground = new SolidColorBrush(Colors.Transparent);

        private ScrollViewer allDayAreaScrollViewer;
        private Canvas allDayAreaPanel;
        private RadRect allDayClipArea;

        private Dictionary<CalendarAppointmentInfo, AppointmentControl> realizedAppointmentPresenters;
        private Dictionary<CalendarAppointmentInfo, AppointmentControl> visibleAppointmentPresenters;
        private Queue<AppointmentControl> recycledAppointments;
        private Queue<AppointmentControl> fullyRecycledAppointments;

        public XamlAllDayAreaLayer()
        {
            this.allDayAreaPanel = new Canvas();
            this.allDayAreaPanel.VerticalAlignment = VerticalAlignment.Top;
            this.allDayAreaScrollViewer = new ScrollViewer();
            this.allDayAreaScrollViewer.Content = this.allDayAreaPanel;
            this.allDayAreaScrollViewer.AllowDrop = true;

            this.realizedAppointmentPresenters = new Dictionary<CalendarAppointmentInfo, AppointmentControl>();
            this.visibleAppointmentPresenters = new Dictionary<CalendarAppointmentInfo, AppointmentControl>();
            this.recycledAppointments = new Queue<AppointmentControl>();
            this.fullyRecycledAppointments = new Queue<AppointmentControl>();
        }

        protected internal override UIElement VisualElement
        {
            get
            {
                return this.allDayAreaScrollViewer;
            }
        }

        internal void UpdateAllDayAreaUI()
        {
            if (this.shouldArrange)
            {
                this.ArrangeVisualElement();
                this.shouldArrange = false;
            }

            var model = this.Owner.Model.multiDayViewModel;
            List<CalendarAppointmentInfo> allDayAppointmentInfos = model.allDayAppointmentInfos;
            if (allDayAppointmentInfos == null || allDayAppointmentInfos.Count == 0)
            {
                this.allDayAreaScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                this.allDayAreaScrollViewer.ClearValue(FrameworkElement.VisibilityProperty);

                this.allDayClipArea = new RadRect(this.allDayAreaScrollViewer.HorizontalOffset, this.allDayAreaScrollViewer.VerticalOffset, this.allDayAreaScrollViewer.Width, model.totalAllDayAreaHeight);
                this.UpdateAllDayAppointments(allDayAppointmentInfos);
            }
        }

        internal void ClearRealizedAppointmentVisuals()
        {
            this.RecycleAppointments(this.realizedAppointmentPresenters.Values);
            this.realizedAppointmentPresenters.Clear();
        }

        internal void ArrangeVisualElement()
        {
            RadCalendar calendar = this.Owner;
            CalendarMultiDayViewModel multiDayViewModel = calendar.Model.multiDayViewModel;

            if (multiDayViewModel.allDayAppointmentInfos != null && multiDayViewModel.allDayAppointmentInfos.Count > 0)
            {
                Size availableCalendarViewSize = calendar.CalendarViewSize;

                double timeRulerWidth = multiDayViewModel.timeRulerWidth;
                double totalWidth = (availableCalendarViewSize.Width - timeRulerWidth) * 2;
                double allDayAreaHeight = multiDayViewModel.totalAllDayAreaHeight;

                this.allDayAreaScrollViewer.Width = 2 * totalWidth;
                this.allDayAreaScrollViewer.Height = allDayAreaHeight;

                int allDayAreaRowCount = multiDayViewModel.allDayAreaRowCount;
                this.allDayAreaPanel.Height = multiDayViewModel.allDayAppointmentInfos.First().layoutSlot.Bottom * allDayAreaRowCount
                    + (allDayAreaRowCount - 1) * calendar.MultiDayViewSettings.AllDayAppointmentSpacing;

                double cellHeight = multiDayViewModel.dayViewLayoutSlot.Height / multiDayViewModel.SpecificColumnCount;
                double topOffset = Math.Abs(cellHeight + multiDayViewModel.dayViewLayoutSlot.Y + calendar.GridLinesThickness);
                Canvas.SetTop(this.allDayAreaScrollViewer, topOffset);
                this.UpdatePanelBackground(calendar.MultiDayViewSettings.AllDayAreaBackground);
            }
            else
            {
                this.allDayAreaPanel.Height = 0;
                this.allDayAreaScrollViewer.Width = 0;
                this.allDayAreaScrollViewer.Height = 0;
            }
        }

        internal void UpdatePanelBackground(Brush background)
        {
            if (this.allDayAreaPanel.Background != background)
            {
                if (background != null)
                {
                    this.allDayAreaPanel.Background = background;
                }
                else
                {
                    this.allDayAreaPanel.Background = XamlAllDayAreaLayer.DefaultBackground;
                }
            }
        }

        protected internal override void DetachUI(Panel parent)
        {
            base.DetachUI(parent);
            this.allDayAreaScrollViewer.ViewChanged -= this.OnAllDayAreaScrollViewerViewChanged;
            this.allDayAreaScrollViewer.RemoveHandler(UIElement.DropEvent, new DragEventHandler(this.OnScrollViewerDropEvent));
            this.allDayAreaScrollViewer.RemoveHandler(UIElement.DragOverEvent, new DragEventHandler(this.OnScrollViewerDragOverEvent));
        }

        protected internal override void AttachUI(Panel parent)
        {
            base.AttachUI(parent);
            this.allDayAreaScrollViewer.ViewChanged += this.OnAllDayAreaScrollViewerViewChanged;
            this.allDayAreaScrollViewer.AddHandler(UIElement.DropEvent, new DragEventHandler(this.OnScrollViewerDropEvent), true);
            this.allDayAreaScrollViewer.AddHandler(UIElement.DragOverEvent, new DragEventHandler(this.OnScrollViewerDragOverEvent), true);
        }

        private void OnScrollViewerDragOverEvent(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            // Check if needed
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
        }

        private void OnScrollViewerDropEvent(object sender, DragEventArgs e)
        {
            var properties = e.DataView.Properties;
            object appControl;
            if (properties.TryGetValue("AppointmentControl", out appControl))
            {
                var droppedPosition = e.GetPosition(this.allDayAreaScrollViewer);
                var hitTestService = this.Owner.hitTestService;
                var dateTime = hitTestService.GetDateFromPoint(droppedPosition);
                var info = ((AppointmentControl)appControl).appointmentInfo;
                var dateTimeAppointment = info.childAppointment as DateTimeAppointment;
                if (dateTime.HasValue && dateTimeAppointment != null)
                {
                    var appDuration = info.ChildAppointment.EndDate - info.ChildAppointment.StartDate;

                    var startTime = dateTime.Value.Date.Add(dateTimeAppointment.StartDate.TimeOfDay);
                    dateTimeAppointment.StartDate = startTime;
                    dateTimeAppointment.EndDate = startTime.AddMilliseconds(appDuration.TotalMilliseconds);
                    if (!dateTimeAppointment.IsAllDay)
                    {
                        dateTimeAppointment.IsAllDay = true;
                    }
                }
                
                ((AppointmentControl)appControl).Opacity = ((AppointmentControl)appControl).opacityCache;
            }
        }

        protected internal override void AddVisualChild(UIElement child)
        {
            this.allDayAreaPanel.Children.Add(child);
        }

        protected internal override void RemoveVisualChild(UIElement child)
        {
            this.allDayAreaPanel.Children.Remove(child);
        }

        private void UpdateAllDayAppointments(List<CalendarAppointmentInfo> allDayAppointmentInfos)
        {
            if (allDayAppointmentInfos == null)
            {
                return;
            }

            foreach (var appointmentInfo in allDayAppointmentInfos)
            {
                AppointmentControl appControl;
                if (this.realizedAppointmentPresenters.TryGetValue(appointmentInfo, out appControl))
                {
                    this.visibleAppointmentPresenters.Add(appointmentInfo, appControl);
                }
            }

            RadCalendar calendar = this.Owner;
            foreach (var appInfo in allDayAppointmentInfos)
            {
                AppointmentControl appointmentControl;
                this.visibleAppointmentPresenters.TryGetValue(appInfo, out appointmentControl);

                if (!this.allDayClipArea.IntersectsWith(appInfo.layoutSlot))
                {
                    if (appointmentControl != null)
                    {
                        this.RecycleAppointmentVisual(appInfo, appointmentControl);
                    }

                    continue;
                }

                if (appointmentControl != null)
                {
                    RadRect layoutSlot = appInfo.layoutSlot;
                    XamlContentLayer.ArrangeUIElement(appointmentControl, layoutSlot, true);
                    continue;
                }

                appointmentControl = this.GetDefaultAllDayAppointmentVisual(appInfo);
                if (appointmentControl != null)
                {
                    appointmentControl.appointmentInfo = appInfo;
                    calendar.PrepareContainerForAppointment(appointmentControl, appInfo);

                    RadRect layoutSlot = appInfo.layoutSlot;
                    XamlContentLayer.ArrangeUIElement(appointmentControl, layoutSlot, true);
                }
            }

            this.RecycleAppointments(this.recycledAppointments);
            this.recycledAppointments.Clear();
            this.visibleAppointmentPresenters.Clear();
        }

        private AppointmentControl GetDefaultAllDayAppointmentVisual(CalendarAppointmentInfo info)
        {
            AppointmentControl visual;
            if (this.recycledAppointments.Count > 0)
            {
                visual = this.recycledAppointments.Dequeue();
            }
            else if (this.fullyRecycledAppointments.Count > 0)
            {
                visual = this.fullyRecycledAppointments.Dequeue();
                visual.ClearValue(AppointmentControl.VisibilityProperty);
            }
            else
            {
                visual = this.CreateDefaultAllDayAppointmentVisual();
            }

            this.realizedAppointmentPresenters.Add(info, visual);
            return visual;
        }

        private AppointmentControl CreateDefaultAllDayAppointmentVisual()
        {
            AppointmentControl appointmentControl = new AppointmentControl();
            appointmentControl.calendar = this.Owner;
            appointmentControl.CanDrag = true;
            appointmentControl.DragStarting += AppointmentControl_DragStarting;
            this.AddVisualChild(appointmentControl);

            return appointmentControl;
        }

        private async void AppointmentControl_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            AppointmentControl appControl = (AppointmentControl)sender;
            args.Data.Properties.Add("AppointmentControl", appControl);

            var scale = (double)Windows.Graphics.Display.DisplayInformation.GetForCurrentView().ResolutionScale / 100;
            var initialDragPoint = args.GetPosition(appControl);
            initialDragPoint.Y /= scale;
            initialDragPoint.X /= scale;
            args.Data.Properties.Add("HitPoint", initialDragPoint);

            var renderTargetBitmap = new RenderTargetBitmap();
            await renderTargetBitmap.RenderAsync(appControl);

            var buffer = await renderTargetBitmap.GetPixelsAsync();
            var bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer,
                BitmapPixelFormat.Bgra8,
                renderTargetBitmap.PixelWidth,
                renderTargetBitmap.PixelHeight,
                BitmapAlphaMode.Premultiplied);
            args.DragUI.SetContentFromSoftwareBitmap(bitmap);

            appControl.opacityCache = appControl.Opacity;
            appControl.Opacity = 0.6;
        }

        private void RecycleAppointments(IEnumerable<AppointmentControl> realizedPresenters)
        {
            foreach (AppointmentControl appControl in realizedPresenters)
            {
                appControl.Visibility = Visibility.Collapsed;
                this.fullyRecycledAppointments.Enqueue(appControl);
            }
        }

        private void RecycleAppointmentVisual(CalendarAppointmentInfo appointmentInfo, AppointmentControl visual)
        {
            this.realizedAppointmentPresenters.Remove(appointmentInfo);
            this.recycledAppointments.Enqueue(visual);
        }

        private void OnAllDayAreaScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            this.UpdateAllDayAreaUI();
        }
    }
}