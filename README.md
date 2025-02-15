# SLC KEMP Monitoring
This is a System Center Operations Manager management pack for KEMP LoadMaster. Works with SCOM 2016+.
More details are coming soon. 

Brief description:
Things you're going to need: 
  1. KEMP LoadMaster discovered via Network Discovery in SCOM.
  2. KEMP LoadMaster API Key. It is used in the 'SLC KEMP API Key (SLC.KEMP.APIRunAsAccount)' Run-As Account. Refer to KEMP docs how to create one.
  3. KEMP Management URL. Override the 'SLC - KEMP LoadMaster Discovery (SLC.KEMP.VLM_Discovery)' discovery parameter '<ManagementFQDN>kemp management fqdn</ManagementFQDN>'. Make sure to only include the FQDN, i.e. 'kemp.domain.com', and not the 'http(-s)://kemp_fqdn'.  This management url should have a valid certificate.

Import Managed Modules MP first and then KEMP Monitoring MP. 

KEMP MP uses managed module to perform date comparison. The reason for that is it's much faster and consumes less resources on SCOM Management Server. 

Discoveries:
  1. KEMP VLM instance - discovery target is System.NetworkManagement.Node, filtered by SysObjId .1.3.6.1.4.1.12196.250.10
  2. A single PowerShell-based discovery for three classes and relationships:
      KEMP Virtual Service (VS) - discovery via KEMP WebAPI
      KEMP Sub VS - discovery via KEMP WebAPI
      KEMP Real Server (RS) - discovery via KEMP WebAPI
Monitors:
  1. Virtual Service State - via SNMP traps
  2. Real Server State - via SNMP traps
  3. SSL Certificate Expiry - via KEMP WebAPI.
