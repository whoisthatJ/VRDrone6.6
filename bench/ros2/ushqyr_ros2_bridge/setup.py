from setuptools import setup

package = "ushqyr_ros2_bridge"

setup(
    name=package,
    version="0.1.0",
    packages=[package],
    data_files=[
        ("share/ament_index/resource_index/packages", ["resource/" + package]),
        ("share/" + package, ["package.xml"]),
    ],
    install_requires=["setuptools"],
    zip_safe=True,
    description="ROS 2 bridge of the Ushqyr AI Bench simulator",
    license="MIT",
    entry_points={"console_scripts": ["bridge = ushqyr_ros2_bridge.bridge:main"]},
)
