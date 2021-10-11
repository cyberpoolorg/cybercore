#!/bin/bash
#################################################################################
# Author: CyberPool                                                             #
#                                                                               #
# Web: https://cyberpool.org                                                    #
#                                                                               #
# Program:                                                                      #
#   Install CyberCore On Ubuntu 20.04 Running Nginx, Dotnet 5.0 And Postgresql  #
#   Setup Genix Wallet With Config Files                                        #
#   v2.1 (Update October, 2021)                                                 #
#                                                                               #
#################################################################################

sleep 2

if ! locale -a | grep en_US.utf8 > /dev/null; then
hide_output locale-gen en_US.UTF-8
fi

export LANGUAGE=en_US.UTF-8
export LC_ALL=en_US.UTF-8
export LANG=en_US.UTF-8
export LC_TYPE=en_US.UTF-8
export NCURSES_NO_UTF8_ACS=1

output() {
  printf "\E[0;33;40m"
  echo $1
  printf "\E[0m"
}

displayErr() {
  echo
  echo $1;
  echo
  exit 1;
}

function EPHYMERAL_PORT() {
	LPORT=32768;
	UPORT=60999;
	while true; do
		MPORT=$[$LPORT + ($RANDOM % $UPORT)];
		(echo "" >/dev/tcp/127.0.0.1/${MPORT}) >/dev/null 2>&1
		if [ $? -ne 0 ]; then
			echo $MPORT;
			return 0;
        	fi
	done
}

wget -L https://raw.githubusercontent.com/cyberpoolorg/cybercore/master/extra/functions.sh
sudo cp -r functions.sh /etc/
source /etc/functions.sh


clear


echo
echo
echo -e "$CYAN=> Installing Needed Packages For Setup To Continue...$COL_RESET"
echo 
sleep 3

hide_output sudo apt -y update 
hide_output sudo apt -y upgrade
apt_install dialog acl nano git apt-transport-https
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


clear


message_box "Genix Coin CyberCore Installer" \
"\n\nThank You for Using This Installer !
\n\nThis Will Install CyberCore with Genix Coin and Web Frontend.
\n\nAfter Answering The Following Questions, Setup Will Be Mostly automated."

dialog --title "Using Domain Name" \
--yesno "Are You Using A Domain Name? Example: example.com ?
Make Sure The DNS Is Updated!" 7 60
response=$?
case $response in
   0) Using_Domain=yes;;
   1) Using_Domain=no;;
   255) echo "[ESC] key pressed.";;
esac

if [[ ("$Using_Domain" == "yes") ]]; then

dialog --title "Using Sub-Domain" \
--yesno "Are You Using A Sub-Domain For The Main Website Domain? Example: pool.cyberpool.org ?
Make Sure The DNS Is Updated!" 7 60
response=$?
case $response in
   0) Using_Sub_Domain=yes;;
   1) Using_Sub_Domain=no;;
   255) echo "[ESC] key pressed.";;
esac

if [ -z "${Domain_Name:-}" ]; then
DEFAULT_Domain_Name=cyberpool.org
input_box "Domain Name" \
"Enter Your Domain Name. If Using A Subdomain Enter The Full Domain As In pool.cyberpool.org
\n\nDo Not Add www. To The Domain Name.
\n\nMake Sure The Domain Is Pointed To This Server Before Continuing !
\n\nDomain Name:" \
${DEFAULT_Domain_Name} \
Domain_Name

if [ -z "${Domain_Name}" ]; then
# user hit ESC/cancel
exit
fi
fi

dialog --title "Install SSL" \
--yesno "Would You Like The System To Install SSL Automatically?" 7 60
response=$?
case $response in
   0) Install_SSL=yes;;
   1) Install_SSL=no;;
   255) echo "[ESC] key pressed.";;
esac
else

Domain_Name=$(get_publicip_from_web_service 4 || get_default_privateip 4)
Using_Sub_Domain=no
Install_SSL=no
fi

if [ -z "${Letsencrypt_Email:-}" ]; then
DEFAULT_Letsencrypt_Email=ssl@gmail.com
input_box "Letsencrypt Email" \
"Enter An Email Address For Letsencrypt (Only When Choosed SSL before).
\n\nLetsencrypt Email:" \
$DEFAULT_Letsencrypt_Email \
Letsencrypt_Email

if [ -z "$Letsencrypt_Email" ]; then
# user hit ESC/cancel
exit
fi
fi

echo 'Using_Domain='"${Using_Domain}"'
Using_Sub_Domain='"${Using_Sub_Domain}"'
Domain_Name='"${Domain_Name}"'
Install_SSL='"${Install_SSL}"'
Letsencrypt_Email='"${Letsencrypt_Email}"'
' | sudo -E tee /etc/web.conf >/dev/null 2>&1


clear


echo
echo -e "$GREEN******************************************************************************$COL_RESET"
echo -e "$GREEN* CyberCore Install Script v2.1                                              *$COL_RESET"
echo -e "$GREEN* Install CyberCore On Ubuntu 20.04 Running Nginx, Dotnet 5.0 And Postgresql *$COL_RESET"
echo -e "$GREEN******************************************************************************$COL_RESET"
echo
sleep 3


echo
echo
echo -e "$CYAN=> Updating System And Installing Required Packages For CyberCore...$COL_RESET"
echo 
sleep 3

hide_output sudo apt -y update 
hide_output sudo apt -y upgrade
hide_output sudo apt -y autoremove
apt_install build-essential software-properties-common curl unzip rar htop
apt_install libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq3-dev libzmq5 screen cmake
apt_install certbot python3-certbot-nginx
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Generate Random Strong Password For Postgresql...$COL_RESET"
echo
echo -e "$GREEN=> Password Will Be Displayed At The End Of Installtion...$COL_RESET"
echo
sleep 3

password=`cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 32 | head -n 1`
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing Nginx Server...$COL_RESET"
echo
sleep 3

if [ -f /usr/sbin/apache2 ]; then
echo -e "Removing apache..."
hide_output apt-get -y purge apache2 apache2-*
hide_output apt-get -y --purge autoremove
fi

apt_install nginx
hide_output sudo systemctl start nginx.service
hide_output sudo systemctl enable nginx.service
hide_output sudo systemctl start cron.service
hide_output sudo systemctl enable cron.service
sudo systemctl status nginx | sed -n "1,3p"
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing Postgresql...$COL_RESET"
echo
sleep 3

apt_install postgresql postgresql-contrib
sudo systemctl status postgresql | sed -n "1,3p"
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing Fail2Ban...$COL_RESET"
echo
sleep 3

apt_install fail2ban
sudo systemctl status fail2ban | sed -n "1,3p"
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing UFW...$COL_RESET"
echo
sleep 3

apt_install ufw
hide_output sudo ufw allow ssh
hide_output sudo ufw allow http
hide_output sudo ufw allow https
hide_output sudo ufw allow 'Nginx Full'
hide_output sudo ufw allow 4000
hide_output sudo ufw allow 3033/tcp
hide_output sudo ufw allow 3133/tcp
hide_output sudo ufw allow 3233/tcp
hide_output sudo ufw allow 3333/tcp
hide_output sudo ufw allow 3433/tcp
hide_output sudo ufw allow 3533/tcp
hide_output sudo ufw allow 3633/tcp
hide_output sudo ufw allow 3733/tcp
hide_output sudo ufw allow 3833/tcp
hide_output sudo ufw allow 3933/tcp
hide_output sudo ufw allow 4033/tcp
hide_output sudo ufw allow 4133/tcp
hide_output sudo ufw allow 4233/tcp
hide_output sudo ufw allow 4333/tcp
hide_output sudo ufw allow 4433/tcp
hide_output sudo ufw allow 4533/tcp
hide_output sudo ufw allow 4633/tcp
hide_output sudo ufw allow 4733/tcp
hide_output sudo ufw allow 4833/tcp
hide_output sudo ufw allow 4933/tcp
hide_output sudo ufw --force enable
sudo systemctl status ufw | sed -n "1,3p"
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing Microsoft Dotnet 5.0...$COL_RESET"
echo
sleep 3

hide_output wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
hide_output sudo dpkg -i packages-microsoft-prod.deb
hide_output sudo apt -y update 
hide_output sudo apt -y upgrade
apt_install dotnet-sdk-5.0
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing CyberCore...$COL_RESET"
echo
echo -e "$GREEN=> Grabbing CyberCore From Github And Build It...$COL_RESET"
echo
sleep 3

cd ~
hide_output git clone https://github.com/cyberpoolorg/cybercore.git
chmod -R +x $HOME/cybercore/
cd $HOME/cybercore/src/Cybercore
hide_output dotnet publish -c Release --framework net5.0  -o ../../../poolcore
cd $HOME/cybercore
hide_output mv examples $HOME/poolcore/
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Creating Bash File For Postgresql...$COL_RESET"
echo
sleep 3

echo '#!/bin/bash
sudo -u postgres createuser --superuser cybercore
sudo -u postgres psql -c "alter user cybercore with encrypted password '"'"''"${password}"''"'"';"
sudo -u postgres createdb cybercore
sudo -u postgres psql -c "alter database cybercore owner to cybercore;"
sudo -u postgres psql -c "grant all privileges on database cybercore to cybercore;"
PGPASSWORD='"${password}"' psql -d cybercore -U cybercore -h 127.0.0.1 -f '"${HOME}"'/cybercore/src/Cybercore/Persistence/Postgres/Scripts/createdb.sql
' | sudo -E tee $HOME/psql.sh >/dev/null 2>&1
sudo chmod -R +x $HOME/psql.sh
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Creating Credentials File For Postgresql...$COL_RESET"
echo
sleep 3

echo '
Your Postgresql Credentials
---------------------------

user     :  cybercore
password : '"${password}"'
database : cybercore
' | sudo -E tee /etc/psql.conf >/dev/null 2>&1
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Creating Postgresql Database...$COL_RESET"
echo
sleep 3

hide_output bash $HOME/psql.sh
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


sleep 2
clear


echo
echo -e "$GREEN**********************************$COL_RESET"
echo -e "$GREEN* CyberCore Install Script v2.1  *$COL_RESET"
echo -e "$GREEN* Install And Setup Genix Wallet *$COL_RESET"
echo -e "$GREEN**********************************$COL_RESET"
echo
sleep 3


echo
echo
echo -e "$CYAN=> Installing Genix Wallet...$COL_RESET"
echo
sleep 3

cd ~
mkdir -p genixcore
cd genixcore
hide_output wget https://github.com/genix-project/genix/releases/download/v2.2.2.1/linux-binaries.zip
hide_output unzip linux-binaries.zip
rm -rf linux-binaries.zip
hide_output sudo install -m 0755 -o root -g root -t /usr/bin *
hide_output sudo install -m 0755 -o root -g root -t /usr/local/bin *
cd ~
hide_output rm -rf genixcore
mkdir -p .genixcore
sleep 2
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Generating RPC Port, RPC User And RPC Password...$COL_RESET"
echo
sleep 3

rpcport=$(EPHYMERAL_PORT)
rpcuser=`cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 32 | head -n 1`
rpcpassword=`cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 32 | head -n 1`
sleep 2
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Generating Genix Wallet Config File...$COL_RESET"
echo
sleep 3

echo 'server=1
daemon=1
listen=1
txindex=1
maxconnections=64
rpcthreads=64
rpcuser='"${rpcuser}"'
rpcpassword='"${rpcpassword}"'
rpcport='"${rpcport}"'
port=43649
rpcallowip=127.0.0.1
rpcbind=0.0.0.0
' | sudo -E tee $HOME/.genixcore/genix.conf >/dev/null 2>&1
sleep 2
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Starting Wallet And Open Port...$COL_RESET"
echo
sleep 3

hide_output sudo ufw allow 43649
hide_output genixd -shrinkdebugfile
sleep 5
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Generating New Wallet Address...$COL_RESET"
echo
sleep 3

wallet="$(genix-cli getnewaddress "")"
sleep 2
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Creating Credentials File For Genix...$COL_RESET"
echo
sleep 3

echo 'Your Genix Wallet Credentials
-----------------------------

rpcport		: '"${rpcport}"'
rpcuser		: '"${rpcuser}"'
rpcpassword	: '"${rpcpassword}"'
Wallet Address	: '"${wallet}"'
' | sudo -E tee /etc/genix.conf >/dev/null 2>&1
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Creating Pool Config File For Genix...$COL_RESET"
echo
sleep 3

echo '{
	"clusterName": "cybercore",
	"logging": {
		"level": "info",
		"enableConsoleLog": true,
		"enableConsoleColors": true,
		"logFile": "pool.log",
		"apiLogFile": "api.log",
		"logBaseDirectory": "'"$HOME/logs/"'",
		"perPoolLogFile": true
	,
	"banning": {
		"manager": "integrated",
		"banOnJunkReceive": false,
		"banOnInvalidShares": false
	},
	"notifications": {
		"enabled": false,
		"email": {
			"host": "smtp.example.com",
			"port": 587,
			"user": "user",
			"password": "password",
			"fromAddress": "info@yourpool.org",
			"fromName": "pool support"
		},
		"pushover": {
			"enabled": false,
			"user": "youruser",
			"token": "yourtoken"
		},
		"admin": {
			"enabled": false,
			"emailAddress": "user@example.com",
			"notifyBlockFound": true
		}
	},
	"persistence": {
		"postgres": {
			"host": "127.0.0.1",
			"port": 5432,
			"user": "cybercore",
			"password": "'"${password}"'",
			"database": "cybercore"
		}
	},
	"paymentProcessing": {
		"enabled": true,
		"interval": 600,
		"shareRecoveryFile": "recovered-shares.txt"
	},
	"api": {
		"enabled": true,
		"listenAddress": "0.0.0.0",
		"port": 4000,
		"adminPort": 5000,
		"metricsIpWhitelist": [""],
		"adminIpWhitelist": [""],
		"rateLimiting": {
			"disabled": true,
			"rules": [{
				"Endpoint": "*",
				"Period": "1s",
				"Limit": 25
			}],
			"ipWhitelist": [""]
		}
	},
	"nicehash": {
		"enableAutoDiff": true
	},
	"pools": [{
		"id": "genix",
		"enabled": true,
		"coin": "genix",
		"address": "'"${wallet}"'",
		"rewardRecipients": [{
			"address": "'"${wallet}"'",
			"percentage": 0.5
		}],
		"blockTimeInterval": 120,
		"paymentInterval": 600,
		"blockRefreshInterval": 333,
		"jobRebroadcastTimeout": 10,
		"clientConnectionTimeout": 600,
		"banning": {
			"enabled": true,
			"time": 600,
			"invalidPercent": 50,
			"checkThreshold": 50
		},
		"ports": {
			"3033": {
				"listenAddress": "0.0.0.0",
				"difficulty": 0.2,
				"name": "GPU Mining",
				"varDiff": {
					"minDiff": 0.1,
					"targetTime": 15,
					"retargetTime": 90,
					"variancePercent": 30,
					"maxDelta": 0.1
				}
			}
		},
		"daemons": [{
			"host": "127.0.0.1",
			"port": '"${rpcport}"',
			"user": "'"${rpcuser}"'",
			"password": "'"${rpcpassword}"'"
		}],
		"paymentProcessing": {
			"enabled": true,
			"minimumPayment": 0.5,
			"payoutScheme": "PROP",
			"payoutSchemeConfig": { "factor": 2.0 }
		}
	}]
}
' | sudo -E tee $HOME/poolcore/config.json >/dev/null 2>&1
sudo chmod -R +x $HOME/poolcore/
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"

echo
echo
echo -e "$CYAN=> Creating Bash File For Letsencrypt...$COL_RESET"
echo
sleep 3

echo '#!/bin/bash
sudo certbot certonly --standalone -d '"${Domain_Name}"' --staple-ocsp -m '"${Letsencrypt_Email}"' --agree-tos --force-renewal
' | sudo -E tee $HOME/ssl.sh >/dev/null 2>&1
sudo chmod -R +x $HOME/ssl.sh
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Creating SSL Certificate If Needed...$COL_RESET"
echo
sleep 3

cd $HOME/cybercore/
hide_output mv extra $HOME/
sudo chmod -R +x $HOME/extra/
cd $HOME/
hide_output bash $HOME/extra/sslcreate.sh
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Building Web File Structure And Copying Files...$COL_RESET"
echo
sleep 3

cd $HOME/extra
source nginxcreate.sh
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Deleting Temp Files...$COL_RESET"
echo
sleep 3

cd ~
hide_output sudo rm -rf psql.sh
hide_output sudo rm -rf cybercore
hide_output sudo rm -rf functions.sh
hide_output sudo rm -rf packages-microsoft-prod.deb
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


sleep 2
clear


echo
echo
echo -e "$GREEN*********************************$COL_RESET"
echo -e "$GREEN* CyberCore Install Script v2.1 *$COL_RESET"
echo -e "$GREEN* Finished YaY !!!              *$COL_RESET"
echo -e "$GREEN*********************************$COL_RESET"
echo 
echo
echo -e "$CYAN WoW That Was Fun, Just Some Reminders$COL_RESET"
echo
echo -e "$YELLOW Your Postgresql User Is $GREEN cybercore$COL_RESET"
echo -e "$YELLOW Your Postgresql Database Is $GREEN cybercore$COL_RESET"
echo -e "$YELLOW Your Postgresql Password Is $GREEN "$password"$COL_RESET"
echo
echo -e "$MAGENTA Your Genix Wallet Address Is $GREEN "$wallet"$COL_RESET"
echo -e "$MAGENTA Your Genix Wallet RPC User Is $GREEN "$rpcuser"$COL_RESET"
echo -e "$MAGENTA Your Genix Wallet RPC Password Is $GREEN "$rpcpassword"$COL_RESET"
echo
echo -e "$GREEN We Saved The Postgresql Credentials In /etc/psql.conf $COL_RESET"
echo -e "$GREEN We Saved The Genix Wallet Credentials In /etc/genix.conf $COL_RESET"
echo
echo -e "$CYAN Example Config Files Are In $HOME/poolcore/examples/ $COL_RESET"
echo -e "$CYAN Pool Sample File With Credentials In $HOME/poolcore/config.json $COL_RESET"
echo -e "$CYAN To Start Cybercore Run : $HOME/poolcore/dotnet Cybercore.dll -c config.json $COL_RESET"
echo
echo
echo -e "$GREEN****************************************************$COL_RESET"
echo -e "$GREEN* YOU MUST REBOOT NOW TO FINALIZE INSTALLATION !!! *$COL_RESET"
echo -e "$GREEN****************************************************$COL_RESET"
echo
echo
