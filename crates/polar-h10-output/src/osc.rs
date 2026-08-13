use std::net::SocketAddr;

use polar_h10_core::AccSample;
use tokio::net::UdpSocket;

use crate::output_stream_name;

pub(crate) const OSC_TARGET: &str = "127.0.0.1:9000";

pub(crate) struct OscPublisher {
    socket: UdpSocket,
    target: SocketAddr,
}

impl OscPublisher {
    pub(crate) async fn connect(target: &str) -> Result<Self, String> {
        let target = target
            .parse()
            .map_err(|error| format!("Invalid OSC target: {error}"))?;
        let socket = UdpSocket::bind("0.0.0.0:0")
            .await
            .map_err(|error| format!("Could not open OSC socket: {error}"))?;
        Ok(Self { socket, target })
    }

    pub(crate) fn send_series<I>(
        &self,
        stream_name: &str,
        metric_id: &str,
        timestamp_ns: u64,
        values: I,
    ) where
        I: IntoIterator<Item = f32>,
    {
        let Some(output_name) = output_stream_name(stream_name, metric_id) else {
            return;
        };
        let path = format!("/{output_name}");
        let packet = encode_floats(&path, timestamp_ns, values);
        let _ = self.socket.try_send_to(&packet, self.target);
    }

    pub(crate) fn send_accelerometer(
        &self,
        stream_name: &str,
        timestamp_ns: u64,
        samples: &[AccSample],
    ) {
        let values = samples.iter().flat_map(|sample| {
            [
                f32::from(sample.x_mg),
                f32::from(sample.y_mg),
                f32::from(sample.z_mg),
            ]
        });
        self.send_series(stream_name, "raw_acc", timestamp_ns, values);
    }
}

fn encode_floats<I>(path: &str, timestamp_ns: u64, values: I) -> Vec<u8>
where
    I: IntoIterator<Item = f32>,
{
    let values: Vec<f32> = values.into_iter().collect();
    let mut packet = Vec::with_capacity(32 + values.len() * 4);
    push_string(&mut packet, path);
    let mut tags = String::from(",h");
    tags.extend(std::iter::repeat_n('f', values.len()));
    push_string(&mut packet, &tags);
    packet.extend_from_slice(&(timestamp_ns as i64).to_be_bytes());
    for value in values {
        packet.extend_from_slice(&value.to_bits().to_be_bytes());
    }
    packet
}

fn push_string(buffer: &mut Vec<u8>, value: &str) {
    buffer.extend_from_slice(value.as_bytes());
    buffer.push(0);
    while !buffer.len().is_multiple_of(4) {
        buffer.push(0);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn packet_is_padded_and_network_endian() {
        let packet = encode_floats("/polar/ecg", 9, [1.5]);
        assert_eq!(packet.len() % 4, 0);
        assert!(packet.starts_with(b"/polar/ecg\0"));
        assert_eq!(
            &packet[packet.len() - 4..],
            &1.5_f32.to_bits().to_be_bytes()
        );
    }

    #[test]
    fn uses_the_canonical_output_name_as_the_osc_path() {
        let name = output_stream_name("participant_07", "raw_ecg").unwrap();
        let packet = encode_floats(&format!("/{name}"), 9, [1.5]);
        assert!(packet.starts_with(b"/participant_07_rawECG\0"));
    }
}
