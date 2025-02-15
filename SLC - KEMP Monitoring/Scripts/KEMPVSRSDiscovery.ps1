param (
$debug
)
$SourceId = "$MPElement$"
$TargetId = "$Target/Id$"
#region Debug logging function
function write-debuglog ($message)	{
	if ($debug -eq $true -and $message)
		{
            $oAPI.LogScriptEvent($ScriptName,$EventNumber,0 ,"$Message")
		}
	}
#endregion
function Get-TopLevelVSList ($listvs) {
if ($listvs.code -ne "200") {

}
else {

return $listvs.VS | ? MasterVSID -eq 0

}


}
function Get-SubVSList ($listvs) {
if ($listvs.code -ne "200") {

}
else {

return $listvs.VS | ? MasterVSID -ne 0

}

}
function Get-RSList ($listvs) {
if ($listvs.code -ne "200") {

}
else {
$rslist = @()

$listvs.VS | ? Rs | select -ExpandProperty Rs | % {$rslist += $_}
return $rslist

}


}
function Get-CertificateList ($kempCerts) {
$allCerts = @()
foreach ($kempCert in $kempCerts) {
[xml]$c = curl "https://$kempName/access/readcert?apikey=$apiKey&cert=$($kempcert.name)&outform=DER" -UseBasicParsing
$allcerts += [System.Security.Cryptography.X509Certificates.X509Certificate2]::new([System.Convert]::FromBase64String($c.Response.Success.Data.certificate)) 
}
return $allCerts
}
function Populate-Properties ($Instance, $Class, $kempObject) {
			foreach ($property in $Class.GetProperties()) {
				$value = $kempObject.($property.Name)
				$Instance.AddProperty($property.id, [System.Convert]::ChangeType($value, $property.SystemType))
			}
}
function Create-SCOMRelationShipInstance ($source, $target, $relationship){
	$relationshipinstance = $Discovery.CreateRelationshipInstance($relationship.Id)
	$relationshipinstance.Source = $source
	$relationshipinstance.Target = $target
	$Discovery.Addinstance($relationshipinstance)
}
function Create-SingleInstance ($SourceObject, $Class) {
		$Instance = $Discovery.CreateClassInstance($Class.Id)
		Populate-Properties $Instance $Class $SourceObject

		switch ($Class.Id){

		$vsClass.Id { 
			#VS class is hosted, so we need to populate extra key props + displayName
			$displayName = if ($SourceObject.NickName) {$SourceObject.NickName} else { $SourceObject.VSAddress }
			$Instance.AddProperty("$MPElement[Name='System!System.Entity']/DisplayName$", $displayName)
			$Instance.AddProperty("$MPElement[Name='SNL!System.NetworkManagement.LogicalDevice']/Key$", $SourceObject.Index)
			$Instance.AddProperty("$MPElement[Name='SNL!System.NetworkManagement.Node']/DeviceKey$", "$Target/Property[Type='SNL!System.NetworkManagement.Node']/DeviceKey$")
			;break}
		$rsClass.Id { 
			#RS class is hosted, so we need to populate extra key props + displayName
			$displayName = if ($SourceObject.DnsName) { $SourceObject.DnsName } else { $SourceObject.Addr }
			$Instance.AddProperty("$MPElement[Name='System!System.Entity']/DisplayName$", $displayName)
			$Instance.AddProperty("$MPElement[Name='SLC.KEMP.VirtualService']/Index$", $SourceObject.VSIndex)
			$Instance.AddProperty("$MPElement[Name='SNL!System.NetworkManagement.ServiceAccessPoint']/Key$", "RS$($SourceObject.RsIndex)")
			$Instance.AddProperty("$MPElement[Name='SNL!System.NetworkManagement.LogicalDevice']/Key$", $SourceObject.VSIndex)
			$Instance.AddProperty("$MPElement[Name='SNL!System.NetworkManagement.Node']/DeviceKey$", "$Target/Property[Type='SNL!System.NetworkManagement.Node']/DeviceKey$")
			;break}
		$kempCertClass.Id {
			#SSLCertificate class is hosted, so we need to populate extra key props + displayName
			$Instance.AddProperty("$MPElement[Name='System!System.Entity']/DisplayName$", $SourceObject.Subject)
			$Instance.AddProperty("$MPElement[Name='SNL!System.NetworkManagement.Node']/DeviceKey$", "$Target/Property[Type='SNL!System.NetworkManagement.Node']/DeviceKey$")
			;break
		}
		default: { break}
	}
		
	return $Instance

}
function Create-SCOMClassInstance ($SourceItemList, $ContainingVS) {
	if (!$SourceItemList) {
		$oAPI.LogScriptEvent($ScriptName, $EventNumber, 1, "Empty Source Item list received. Exiting...")
		exit
	}
	foreach ($SourceItem in $SourceItemList) {
		if ($SourceItem.GetType() -eq [System.Security.Cryptography.X509Certificates.X509Certificate2] ) { 
			$Instance = Create-SingleInstance $SourceItem $kempCertClass
			$Discovery.AddInstance($Instance)
		}
		else {
			$Instance = Create-SingleInstance $SourceItem $vsClass
			$Discovery.AddInstance($Instance)

			if ($ContainingVS) {
				Create-SCOMRelationShipInstance $ContainingVS $Instance $vSContainsSubVS
			}
			if ($SourceItem.MasterVS -ne 0) {
				foreach ($subVS in $SourceItem.SubVS) {
				Create-SCOMClassInstance ($vs2.vs | ? Index -eq $subVS.VSIndex) $Instance
				}
			}
			if ($SourceItem.Rs){
				foreach ($realServer in $SourceItem.Rs) {
					$RSInstance = Create-SingleInstance $realServer $rsClass
					$Discovery.AddInstance($RSInstance)
				}
			}
		}
	}
}


#region environment set up
$ScriptName = "KEMPVSRSDiscovery.ps1"
$EventNumber = "7001"
$Message =	"discovery has started.`nSource ID: $SourceId`nManaged Entity ID: $TargetId"
$oAPI = New-Object -comObject 'MOM.ScriptAPI'
$oAPI.LogScriptEvent($ScriptName,$EventNumber,0 ,"$Message")
$Discovery = $oAPI.CreateDiscoveryData(0, $SourceId, $TargetId)
#endregion 
#region SCOM binaries
$SCOMServerKey = "HKLM:\SOFTWARE\Microsoft\System Center Operations Manager\12\Setup\Server"
$SDKBinaries = Join-Path (Get-ItemProperty $SCOMServerKey).InstallDirectory "SDK Binaries"
[System.Reflection.Assembly]::LoadFile("$SDKBinaries\Microsoft.EnterpriseManagement.Core.dll")
[System.Reflection.Assembly]::LoadFile("$SDKBinaries\Microsoft.EnterpriseManagement.OperationsManager.dll")
[System.Reflection.Assembly]::LoadFile("$SDKBinaries\Microsoft.EnterpriseManagement.Runtime.dll")
#endregion
#region KEMP query
write-debuglog "Preparing KEMP environment"
#$kempName = "$Target/Property[Type='System!System.Entity']/DisplayName$"
$kempName = "$Target/Property[Type='SLC.KEMP.VLM']/ManagementFQDN$"
if ([string]::IsNullOrWhiteSpace($kempName)) {
$kempName = (Resolve-DnsName "$Target/Property[Type='System!System.Entity']/DisplayName$").Name
}
$apiKey = "$RunAs[Name='SLC.KEMP.APIRunAsAccount']/Password$"
$reqBody = @"
{
"apikey": "$apiKey",
"cmd" : "listvs"
}
"@
$apiv2Url = "https://$kempName/accessv2"
$certUrl = "https://$kempName/access/listcert?apikey=$apiKey"

try {
write-debuglog "Reading data from KEMP. APIv2 URL: $apiv2Url`n$($apiKey.GetHashCode())"
$vss2 = curl $apiv2Url -Method Post -Body $reqBody -UseBasicParsing
write-debuglog "Reading certificate data from KEMP. Cert URL: https://$kempName/access/listcert`n$($apiKey.GetHashCode())"
$kempCerts = curl $certUrl -UseBasicParsing
if ($vss2.StatusCode -eq 200) {

$vs2 = ConvertFrom-Json $vss2.Content
$topVS = Get-TopLevelVSList $vs2
write-debuglog "Top-level VS count: $($topVS.Count)"
$subVS = Get-SubVSList $vs2
write-debuglog "Sub VS count: $($subVS.Count)"
$RSs = Get-RSList $vs2
write-debuglog "Real Server count: $($RSs.Count)"

}
else {
	$oAPI.LogScriptEvent($ScriptName,$EventNumber,1 ,"Failed to query VS via KEMP API.`HTTP $($vss2).StatusCode $($vss2).StatusDescription")
	$Discovery
	exit
}
if ($kempCerts.StatusCode -eq 200) {


$certificates = Get-CertificateList ([xml]$kempCerts.Content).Response.Success.Data.cert 
write-debuglog "KEMP SSL Certificates count: $($certificates.Count)"
}
else {
	$oAPI.LogScriptEvent($ScriptName,$EventNumber,1 ,"Failed to query certificates via KEMP API.`HTTP $($vss2).StatusCode $($vss2).StatusDescription")
	$Discovery
	exit
}
}
catch {
	$oAPI.LogScriptEvent($ScriptName,$EventNumber,1 ,"Failed to query KEMP API.`n$_")
	$Discovery
	exit
}
#endregion




#region SCOM CLass Instances
$mg = [Microsoft.EnterpriseManagement.ManagementGroup]::new("localhost")
$vsClass = $mg.EntityTypes.GetClass([guid]::Parse("02f7fc7e-703c-93d7-1d08-594e4f251b8b")) # KEMP VS
$rsClass = $mg.EntityTypes.GetClass([guid]::Parse("44f02864-c4bf-ab6a-9bd1-1a0e6714a239")) # KEMP RS
$vSContainsSubVS = $mg.EntityTypes.GetRelationshipClass([guid]::Parse("e88f3f08-6130-92eb-92e6-0a82bcbdebc6")) # SLC.KEMP.VirtualService_Contains_SubVirtualService relationship
$kempCertClass = $mg.EntityTypes.GetClass([guid]::Parse("682eac6c-3d3d-51d3-c243-3236a18304bf")) #KEMP SSL Certficate

write-debuglog "Creating SCOM Instances. $vsClass, $rsClass, $kempCertClass"
Create-SCOMClassInstance $vs2.VS $null
Create-SCOMClassInstance $certificates $null
#endregion 

$Discovery