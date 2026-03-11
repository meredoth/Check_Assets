# Check_Assets

**Last Upadte based on the PDF published by Unity on March 10**

Use this tool to determine whether your account contains any assets published by companies identified as Chinese publishers.

## Setup

* Place the script inside an Editor folder within your Assets directory.

## How It Works

After importing the script, a new menu item called “My Asset Store Library” will appear under the Window menu.

When you open it, a window with a single button will be displayed:

<img width="727" height="698" alt="Asset_Checker1" src="https://github.com/user-attachments/assets/9907735a-0820-4d5c-9ef9-60ce1bd8ebbc" />

Clicking this button attempts to download information about the assets associated with the account you are currently logged into. After retrieving the asset names, the script processes each entry to extract publisher information. This step may take some time.

Once the data has been processed, the results are logged in the Console window and also appear in a new sortable column called Flagged (thanks to Lane Fox).

A file containing deprecated packages is no longer required, as the script now has the deprecated package IDs hardcoded.

## Disclaimer

* This script has not been thoroughly tested. Its ability to correctly identify every publisher, cannot be guaranteed as no real assets were available for testing, so validation was performed using dummy assets.

* The script relies heavily on reflection to access internal Unity members. Since Unity frequently changes its internal APIs, compatibility is not guaranteed. It has been tested with Unity 6.3.10 and will likely fail in earlier versions and potentially in future versions.