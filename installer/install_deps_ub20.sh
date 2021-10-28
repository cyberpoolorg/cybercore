#!/bin/bash
#################################################################################
# Author: CyberPool                                                             #
#                                                                               #
# Web: https://cyberpool.org                                                    #
#                                                                               #
# Program:                                                                      #
#   Install CyberCore On Ubuntu 20.04 Running Nginx, Dotnet 5.0 And Postgresql  #
#   v2.0 (Update October, 2021)                                                 #
#                                                                               #
#################################################################################

sleep 2

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

wget -L https://raw.githubusercontent.com/cyberpoolorg/cybercore/master/extra/functions.sh
sudo cp -r functions.sh /etc/
source /etc/functions.sh

clear

echo
echo -e "$GREEN******************************************************************************$COL_RESET"
echo -e "$GREEN* CyberCore Install Script v2.0                                              *$COL_RESET"
echo -e "$GREEN* Install CyberCore On Ubuntu 20.04 Running Nginx, Dotnet 5.0 And Postgresql *$COL_RESET"
echo -e "$GREEN******************************************************************************$COL_RESET"
echo
sleep 3


echo
echo
echo -e "$CYAN=> Updating System And Installing Required Packages $COL_RESET"
echo 
sleep 3

hide_output sudo apt -y update 
hide_output sudo apt -y upgrade
hide_output sudo apt -y autoremove
apt_install apt-transport-https build-essential software-properties-common curl unzip rar htop git
apt_install libssl-dev pkg-config libboost-all-dev libsodium-dev libzmq3-dev libzmq5 screen cmake
apt_install certbot python3-certbot-nginx dialog pwgen
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"

echo
echo
echo -e "$CYAN=> Installing Nginx Server$COL_RESET"
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
sleep 2

echo '
map $http_user_agent $blockedagent {
  default         0;
  ~*malicious     1;
  ~*bot           1;
  ~*backdoor      1;
  ~*crawler       1;
  ~*bandit        1;
}
' | sudo -E tee /etc/nginx/blockuseragents.rules >/dev/null 2>&1
sudo systemctl status nginx | sed -n "1,3p"
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing Postgresql$COL_RESET"
echo
sleep 3

apt_install postgresql postgresql-contrib
sudo systemctl status postgresql | sed -n "1,3p"
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing Fail2Ban$COL_RESET"
echo
sleep 3

apt_install fail2ban
sudo systemctl status fail2ban | sed -n "1,3p"
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing UFW$COL_RESET"
echo
sleep 3

apt_install ufw
hide_output sudo ufw allow ssh
hide_output sudo ufw allow http
hide_output sudo ufw allow https
hide_output sudo ufw allow 'Nginx Full'
hide_output sudo ufw allow 3033/tcp
hide_output sudo ufw allow 3133/tcp
hide_output sudo ufw --force enable
sudo systemctl status ufw | sed -n "1,3p"
sleep 2
echo
echo -e "$GREEN=> Done...$COL_RESET"


echo
echo
echo -e "$CYAN=> Installing Microsoft Dotnet 5.0$COL_RESET"
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


hide_output sudo rm -rf functions.sh
hide_output sudo rm -rf packages-microsoft-prod.deb


echo
echo
echo -e "$GREEN*********************************$COL_RESET"
echo -e "$GREEN* CyberCore Install Script v2.0 *$COL_RESET"
echo -e "$GREEN* Finished YAY !!!              *$COL_RESET"
echo -e "$GREEN*********************************$COL_RESET"
echo 
echo
echo -e "$CYAN WoW that was fun !!! $COL_RESET"
echo
echo
echo -e "$RED****************************************************$COL_RESET"
echo -e "$RED* YOU MUST REBOOT NOW TO FINALIZE INSTALLATION !!! *$COL_RESET"
echo -e "$RED****************************************************$COL_RESET"
echo
echo
