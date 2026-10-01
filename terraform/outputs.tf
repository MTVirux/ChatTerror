output "relay_public_ipv4" {
  description = "Public IPv4 of the relay VM."
  value       = hcloud_server.relay.ipv4_address
}

output "relay_url" {
  description = "Relay URL to set in the plugin and open on the phone."
  value       = "https://${local.fqdn}"
}
