"""Create short-lived loopback-only recording credentials outside user profiles."""
from pathlib import Path
import datetime, ipaddress, secrets, sys
from cryptography import x509
from cryptography.x509.oid import NameOID
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import pkcs12
p=Path(sys.argv[1]).resolve()
if 'users' in [x.lower() for x in p.parts]: raise ValueError('Use application-owned scratch')
p.mkdir(parents=True,exist_ok=True)
key=rsa.generate_private_key(public_exponent=65537,key_size=3072)
name=x509.Name([x509.NameAttribute(NameOID.COMMON_NAME,'Tlamatini RC isolated video lab')])
now=datetime.datetime.now(datetime.timezone.utc)
cert=(x509.CertificateBuilder().subject_name(name).issuer_name(name).public_key(key.public_key())
 .serial_number(x509.random_serial_number()).not_valid_before(now-datetime.timedelta(minutes=5))
 .not_valid_after(now+datetime.timedelta(days=2))
 .add_extension(x509.SubjectAlternativeName([x509.IPAddress(ipaddress.ip_address(a)) for a in ['127.0.0.1','127.0.0.2']]),False)
 .sign(key,hashes.SHA256()))
password=secrets.token_hex(32)
(p/'cert.pem').write_bytes(cert.public_bytes(serialization.Encoding.PEM))
(p/'key.pem').write_bytes(key.private_bytes(serialization.Encoding.PEM,serialization.PrivateFormat.PKCS8,serialization.NoEncryption()))
(p/'cert.pfx').write_bytes(pkcs12.serialize_key_and_certificates(b'rc-video',key,cert,None,serialization.BestAvailableEncryption(password.encode())))
(p/'password.txt').write_text(password)
print('Created two-day test certificate for the two loopback authorities. Secrets not printed.')
