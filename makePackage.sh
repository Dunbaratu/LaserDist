# Bash script (that should work in gitbash on Windows) that 
# will crate the export ZIP file for installing the mod.

# Run with "bash makePackage.sh" from this directory.


# ------------------------------------------------------------
# CHANGE THE FOLLOWING SETTINGS FOR YOUR OWN FOLDER LOCATIONS:
# ------------------------------------------------------------

CMD_ZIP="C:/Program Files/7-Zip/7z.exe"
CMD_ZIP_ARGS="-tzip"

# Location where your KSP game is installed:
#INSTALL_GAME_DIR="D:/KSP mod sandbox"
INSTALL_GAME_DIR="D:/KSP_galileo"

# Change this to "yes" if you want to install the ZIP to your game after it gets made:
# Change this to "no" if you want to suppress the install to your game and just make the ZIP only:
DO_INSTALL="yes"  
# DO_INSTALL="no" 

# ------------------------------------------------------------
# YOU SHOULDN'T NEED TO ALTER THE LINES FROM HERE DOWN:
# ------------------------------------------------------------

EXPORT_DIR="./GameData"
MOD_NAME="LaserDist"

if [ -e "$EXPORT_DIR" ]
then
  echo "$EXPORT_DIR exists.  Clearing it out."
  rm -r "$EXPORT_DIR"
fi

if [ -e "${MOD_NAME}.zip" ]
then
  echo "${MOD_NAME}.zip exists.  Removing it."
  rm "${MOD_NAME}.zip"
fi

echo "-----------------------------------------"
echo "Staging files for ZIPping in $EXPORT_DIR."
echo "-----------------------------------------"
mkdir "$EXPORT_DIR"
mkdir "${EXPORT_DIR}/${MOD_NAME}"
cp  README.md            "${EXPORT_DIR}/${MOD_NAME}"
cp  LICENSE              "${EXPORT_DIR}/${MOD_NAME}"
cp -r Parts "${EXPORT_DIR}/${MOD_NAME}/"
cp -r LaserDist.version "${EXPORT_DIR}/${MOD_NAME}/"
mkdir "${EXPORT_DIR}/${MOD_NAME}/Plugins"
cp -r src/LaserDist/bin/Debug/LaserDist.dll "${EXPORT_DIR}/${MOD_NAME}/Plugins"
"$CMD_ZIP" "$CMD_ZIP_ARGS" a "${MOD_NAME}.zip" "${EXPORT_DIR}"

if [ "$DO_INSTALL" = "yes" ]
then
  echo "-----------------------------------------------"
  echo "ZIP File made, now installing to your KSP Game."
  echo "-----------------------------------------------"
  cp "${MOD_NAME}.zip" "${INSTALL_GAME_DIR}"
  cd "${INSTALL_GAME_DIR}"
  "$CMD_ZIP" x -y "${MOD_NAME}.zip" 
  rm "${MOD_NAME}.zip"
else
  echo "----------------------"
  echo "Skipping Install Step."
  echo "----------------------"
fi
