﻿using PhilipDaubmeier.DigitalstromClient.Model;
using PhilipDaubmeier.DigitalstromClient.Model.Core;
using PhilipDaubmeier.DigitalstromClient.Model.Events;
using PhilipDaubmeier.DigitalstromClient.Model.RoomState;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhilipDaubmeier.DigitalstromClient.Network
{
    public class DigitalstromSceneClient : WebApiWorkerThreadBase<DssEvent>
    {
        public event EventHandler ModelChanged;

        private volatile bool subscribed;
        private int subscriptionId;
        private DigitalstromWebserviceClient apiClient;

        private IEnumerable<IEventName> eventsToSubscribe;

        public ApartmentState Scenes { get; set; }

        public DigitalstromSceneClient(IDigitalstromConnectionProvider connectionProvider, IEnumerable<IEventName> eventsToSubscribe = null)
            : this(new DigitalstromWebserviceClient(connectionProvider), eventsToSubscribe)
        { }

        public DigitalstromSceneClient(DigitalstromWebserviceClient client, IEnumerable<IEventName> eventsToSubscribe = null) : base()
        {
            Scenes = new ApartmentState();
            ApiEventRaised += HandleDssApiEvent;
            subscriptionId = 42;// new Random(Convert.ToInt32(DateTime.UtcNow.Ticks % int.MaxValue)).Next(10, 100);
            apiClient = client;
            subscribed = false;

            this.eventsToSubscribe = new List<IEventName>()
            {
                (SystemEventName)SystemEventName.EventType.CallScene,
                (SystemEventName)SystemEventName.EventType.CallSceneBus
            };
            if (eventsToSubscribe != null)
                ((List<IEventName>)this.eventsToSubscribe).AddRange(eventsToSubscribe);

            LoadApartment();
        }
        
        private async void LoadApartment()
        {
            await LoadApartmentScenes();
            await LoadApartmentSensors();

            OnModelChanged();
        }

        private async Task LoadApartmentScenes()
        {
            var apartment = await apiClient.GetZonesAndLastCalledScenes();
            if (apartment == null || apartment.zones == null)
                return;

            foreach (var zone in apartment.zones)
            {
                if (zone == null || zone.groups == null || zone.groups.Count <= 0)
                    continue;

                RoomState roomState = new RoomState();
                foreach (var groupstructure in zone.groups)
                {
                    if (groupstructure == null)
                        continue;

                    roomState[(Group)groupstructure.group].Value = groupstructure.lastCalledScene;
                }
                Scenes[zone.ZoneID] = roomState;
            }
        }

        private async Task LoadApartmentSensors()
        {
            var apartment = await apiClient.GetZonesAndSensorValues();
            if (apartment == null || apartment.zones == null)
                return;

            foreach (var zone in apartment.zones)
            {
                if (zone == null || zone.sensor == null || zone.sensor.Count <= 0)
                    continue;

                RoomState roomState = Scenes[zone.ZoneID];
                if (roomState == null)
                    roomState = new RoomState();
                foreach (var sensor in zone.sensor)
                {
                    if (sensor == null)
                        continue;

                    roomState[sensor.sensorType].Value = sensor;
                }
                Scenes[zone.ZoneID] = roomState;
            }
        }

        public void callScene(Zone zone, Group group, Scene scene)
        {
            QueueAction(async () => await apiClient.CallScene(zone, group, scene));
        }

        protected override async Task<IEnumerable<DssEvent>> ProcessEventPolling()
        {
            if (!subscribed)
            {
                foreach (var eventName in eventsToSubscribe)
                    await apiClient.Subscribe(eventName, subscriptionId);
                subscribed = true;
            }
            var events = await apiClient.PollForEvents(subscriptionId, 60000);
            if (events.events == null)
                return new List<DssEvent>();

            return events.events;
        }

        private void HandleDssApiEvent(object sender, ApiEventRaisedEventArgs<DssEvent> args)
        {
            if (args.ApiEvent == null)
                return;
            
            switch (args.ApiEvent.systemEvent.type)
            {
                case SystemEventName.EventType.CallSceneBus: goto case SystemEventName.EventType.CallScene;
                case SystemEventName.EventType.CallScene:
                    HandleDssCallSceneEvent(args.ApiEvent); break;
            }
        }

        private void HandleDssCallSceneEvent(DssEvent dssEvent)
        {
            var props = dssEvent.properties;
            if (props == null)
                return;

            Scenes[props.zone, props.group].Value = props.scene;
            OnModelChanged();
        }

        private void OnModelChanged()
        {
            if (ModelChanged == null)
                return;

            ModelChanged(this, null);
        }
    }
}