using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BleakBridge
{
    public class Bridge
    {
        public Dictionary<Guid, TypedEventHandler<GattCharacteristic, byte[]>> callbacks;
        public Bridge()
        {
            callbacks = new Dictionary<Guid, TypedEventHandler<GattCharacteristic, byte[]>>();
        }

        public async Task<BluetoothLEDevice> BluetoothLEDeviceFromIdAsync(string id)
        {
            BluetoothLEDevice ble = await BluetoothLEDevice.FromIdAsync(id);
            GattDeviceServicesResult results = await ble.GetGattServicesAsync();
            return ble;
        }

        public async Task<GattDeviceServicesResult> GetGattServicesAsync(BluetoothLEDevice ble)
        {
            GattDeviceServicesResult results = await ble.GetGattServicesAsync();
            return results;
        }

        public async Task<GattCharacteristicsResult> GetCharacteristicsAsync(GattDeviceService service)
        {
            GattCharacteristicsResult results = await service.GetCharacteristicsAsync();
            return results;
        }

        public async Task<GattDescriptorsResult> GetDescriptorsAsync(GattCharacteristic characteristic)
        {
            var x = characteristic.GetDescriptorsAsync();
            GattDescriptorsResult results = await characteristic.GetDescriptorsAsync();
            return results;
        }

        public async Task<Tuple<List<GattDeviceService>, List<GattCharacteristic>, List<GattDescriptor>>> ResolveServices(BluetoothLEDevice ble)
        {
            GattDeviceServicesResult results = await ble.GetGattServicesAsync();
            var services = new List<GattDeviceService>();
            var characteristics = new List<GattCharacteristic>();
            var descriptors = new List<GattDescriptor>();
            var charTasks = new List<IAsyncOperation<GattCharacteristicsResult>>();
            foreach (var service in results.Services)
            {
                GattCharacteristicsResult characteristics_results = await service.GetCharacteristicsAsync();
                foreach (var characteristic in characteristics_results.Characteristics)
                {
                    GattDescriptorsResult descriptor_results = await characteristic.GetDescriptorsAsync();
                    descriptors.AddRange(descriptor_results.Descriptors);
                }
                characteristics.AddRange(characteristics_results.Characteristics);
            }
            services.AddRange(results.Services);

            return new Tuple<List<GattDeviceService>, List<GattCharacteristic>,List<GattDescriptor>>(services, characteristics, descriptors);
        }

        #region Characteristics

        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            if (this.callbacks.ContainsKey(sender.Uuid))
            {
                var reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte[] output = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(output);
                this.callbacks[sender.Uuid](sender, output);
            }
        }

        public async Task<Tuple<GattCommunicationStatus, byte[]>> ReadCharacteristicValueAsync(GattCharacteristic characteristic)
        {
            GattReadResult result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            byte[] output = null;
            if (result.Status == GattCommunicationStatus.Success)
            {
                var reader = DataReader.FromBuffer(result.Value);
                output = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(output);
            }
            else
            {
                output = new byte[0];
            }
            
            return new Tuple<GattCommunicationStatus, byte[]>(result.Status, output);
        }

        public async Task<GattCommunicationStatus> WriteCharacteristicValueAsync(GattCharacteristic characteristic, byte[] data, bool withResponse)
        {
            var writer = new DataWriter();
            writer.WriteBytes(data);
            if (withResponse)
            {
                GattWriteResult result = await characteristic.WriteValueWithResultAsync(writer.DetachBuffer());
                return result.Status;
            }
            else
            {
                return await characteristic.WriteValueAsync(writer.DetachBuffer());
            }
        }

        public async Task<GattCommunicationStatus> StartNotify(GattCharacteristic characteristic, TypedEventHandler<GattCharacteristic, byte[]> callback)
        {
            // initialize status
            GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
            var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
            if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
            {
                cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            }

            else if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            {
                cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
            }

            status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);
            if (status == GattCommunicationStatus.Success)
            {
                // Server has been informed of clients interest.
                try
                {
                    this.callbacks[characteristic.Uuid] = callback;
                    characteristic.ValueChanged += Characteristic_ValueChanged;
                }
                catch (UnauthorizedAccessException ex)
                {
                    // This usually happens when a device reports that it support indicate, but it actually doesn't.
                    // TODO: Do not use Indicate? Return with Notify?
                    return GattCommunicationStatus.AccessDenied;
                }
            }
            return status;
        }

        public async Task<GattCommunicationStatus> StopNotify(GattCharacteristic characteristic)
        {
            var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.None);
            if (result == GattCommunicationStatus.Success)
            {
                this.callbacks.Remove(characteristic.Uuid);
                characteristic.ValueChanged -= Characteristic_ValueChanged;
            }
            return result;
        }

        #endregion

        #region Descriptors

        public async Task<Tuple<GattCommunicationStatus, byte[]>> ReadDescriptorValueAsync(GattDescriptor descriptor)
        {
            GattReadResult result = await descriptor.ReadValueAsync(BluetoothCacheMode.Uncached);
            byte[] output = null;
            if (result.Status == GattCommunicationStatus.Success)
            {
                var reader = DataReader.FromBuffer(result.Value);
                output = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(output);
            }
            else
            {
                output = new byte[0];
            }

            return new Tuple<GattCommunicationStatus, byte[]>(result.Status, output);
        }

        public async Task<GattCommunicationStatus> WriteDescriptorValueAsync(GattDescriptor descriptor, byte[] data, bool withResponse)
        {
            var writer = new DataWriter();
            writer.WriteBytes(data);
            if (withResponse)
            {
                GattWriteResult result = await descriptor.WriteValueWithResultAsync(writer.DetachBuffer());
                return result.Status;
            }
            else
            {
                return await descriptor.WriteValueAsync(writer.DetachBuffer());
            }
        }

        #endregion
    }
}
